using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.ClosingIssues;
using Credfeto.Workflow.GitHub.Editing;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.Labels;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Mirrors cfwf pr sync-labels: the labels of the issues a pull request closes are added to it one at a time, in the
// repository's own spelling, and none is ever removed. Priority labels are added too, because they are the issue's own
public sealed class PrSyncLabelsService : IPrSyncLabelsService
{
    private readonly IClosingIssueLabelsService _closingIssueLabels;

    private readonly ILabelListService _labelList;

    private readonly IGitHubRestClient _restClient;

    public PrSyncLabelsService(
        IGitHubRestClient restClient,
        IClosingIssueLabelsService closingIssueLabels,
        ILabelListService labelList
    )
    {
        this._restClient = restClient;
        this._closingIssueLabels = closingIssueLabels;
        this._labelList = labelList;
    }

    public ValueTask<PrSyncLabelsResult> SyncAsync(
        string repository,
        int pullRequest,
        bool assignSelf,
        CancellationToken cancellationToken
    )
    {
        // Every argument is validated before the first GitHub call, so invalid input never costs one
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (pullRequest <= 0)
        {
            throw ParameterValidationException.For(parameterName: "pr", rule: "must be above 0");
        }

        EditRequest request = new(
            Repository: validRepository,
            Number: pullRequest,
            Title: null,
            Body: null,
            Priority: null,
            AssignSelf: assignSelf
        );

        return this.SyncValidAsync(request: request, cancellationToken: cancellationToken);
    }

    private async ValueTask<PrSyncLabelsResult> SyncValidAsync(EditRequest request, CancellationToken cancellationToken)
    {
        EditState state = await this.ReadPullRequestAsync(request: request, cancellationToken: cancellationToken);

        ClosingIssueLabelsResponse closing = await this._closingIssueLabels.ReadAsync(
            repository: request.Repository,
            pullRequest: request.Number,
            cancellationToken: cancellationToken
        );

        SyncProgress progress = new(Url: state.Url, FailedReferences: closing.FailedReferences);
        string? login = null;

        if (request.AssignSelf)
        {
            login = await this.ReadLoginAsync(request: request, cancellationToken: cancellationToken);

            // Assigning is how an item is claimed, so one claimed by someone else is refused before any label is added
            if (ItemEdit.CheckAssignee(state: state, login: login) is { } refusal)
            {
                return Refused(progress: progress, refusal: refusal);
            }
        }

        LabelPlan plan = await this.PlanAsync(
            repository: request.Repository,
            wanted: closing.Labels,
            cancellationToken: cancellationToken
        );

        return await this.WriteAsync(
            request: request,
            state: state,
            login: login,
            plan: plan,
            progress: progress,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<EditState> ReadPullRequestAsync(EditRequest request, CancellationToken cancellationToken)
    {
        PullRequestEditResponse response;

        try
        {
            response = await this._restClient.GetAsync(
                relativeUri: $"repos/{request.Repository}/pulls/{request.NumberText}",
                responseTypeInfo: GitHubJsonContext.Default.PullRequestEditResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new PullRequestNotFoundException(
                message: $"the pull request {request.Reference} was not found, or the caller cannot see it",
                innerException: exception
            );
        }
        catch (Exception exception) when (ItemEdit.IsGitHubFailure(exception))
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw Unreadable(request: request, exception: exception);
        }

        return ItemEdit.ToState(
            request: request,
            urlPattern: PrEditService.PullRequestUrl(),
            number: response.Number,
            htmlUrl: response.HtmlUrl,
            title: response.Title,
            body: response.Body,
            assigneeLogins: response.Assignees?.Select(static assignee => assignee?.Login),
            labelNames: response.Labels?.Select(static label => label?.Name)
        ) ?? throw Unreadable(request: request, exception: null);
    }

    private async ValueTask<string> ReadLoginAsync(EditRequest request, CancellationToken cancellationToken)
    {
        return await ItemEdit.ReadLoginAsync(restClient: this._restClient, cancellationToken: cancellationToken)
            ?? throw new PrSyncLabelsFailedException(
                $"could not read the signed-in account, so nothing was written to {request.Reference}"
            );
    }

    // The repository's labels are only read when there is a label to add; each closing issue label is matched against
    // them without regard to case, and one the repository does not have is skipped rather than created
    private async ValueTask<LabelPlan> PlanAsync(
        string repository,
        IReadOnlyList<string> wanted,
        CancellationToken cancellationToken
    )
    {
        // The reader already leaves out Blocked and On Hold; they are left out again here because copying either onto
        // the pull request would stop it, and this tool must never do that whatever the reader returns
        IReadOnlyList<string> included = [.. wanted.Where(ClosingIssueLabelsService.IsIncluded)];

        if (included.Count == 0)
        {
            return new(Add: [], Skipped: []);
        }

        LabelListResponse existing = await this._labelList.ListAsync(
            repository: repository,
            cancellationToken: cancellationToken
        );

        Dictionary<string, string> spelling = [with(StringComparer.OrdinalIgnoreCase)];

        foreach (string label in existing.Labels)
        {
            spelling.TryAdd(key: label, value: label);
        }

        HashSet<string> seen = [with(StringComparer.OrdinalIgnoreCase)];
        List<string> add = [];
        List<string> skipped = [];

        foreach (string label in included.Where(seen.Add))
        {
            if (spelling.TryGetValue(key: label, value: out string? name))
            {
                add.Add(name);
            }
            else
            {
                skipped.Add(label);
            }
        }

        return new(Add: add, Skipped: skipped);
    }

    private async ValueTask<PrSyncLabelsResult> WriteAsync(
        EditRequest request,
        EditState state,
        string? login,
        LabelPlan plan,
        SyncProgress progress,
        CancellationToken cancellationToken
    )
    {
        List<string> added = [];

        foreach (string label in plan.Add)
        {
            string? failure = await this.AddLabelAsync(
                request: request,
                label: label,
                cancellationToken: cancellationToken
            );

            if (failure is not null)
            {
                return Failed(
                    progress: progress,
                    plan: plan,
                    added: added,
                    assigned: false,
                    message: $"could not add the label {label} to {request.Reference}: {failure}"
                );
            }

            added.Add(label);
        }

        bool assigned = false;

        if (login is not null)
        {
            string? failure = await this.AssignAsync(
                request: request,
                state: state,
                login: login,
                cancellationToken: cancellationToken
            );

            if (failure is not null)
            {
                return Failed(
                    progress: progress,
                    plan: plan,
                    added: added,
                    assigned: false,
                    message: $"could not assign {login} to {request.Reference}: {failure}"
                );
            }

            assigned = true;
        }

        return Finished(request: request, progress: progress, plan: plan, added: added, assigned: assigned);
    }

    // One label a call adds to the labels the pull request has, so nothing is ever removed; GitHub treats a label it
    // already carries as no change
    private async ValueTask<string?> AddLabelAsync(
        EditRequest request,
        string label,
        CancellationToken cancellationToken
    )
    {
        try
        {
            _ = await this._restClient.PostAsync(
                relativeUri: $"repos/{request.Repository}/issues/{request.NumberText}/labels",
                body: new LabelAddRequest([label]),
                requestTypeInfo: GitHubJsonContext.Default.LabelAddRequest,
                responseTypeInfo: GitHubJsonContext.Default.IssueLabelResponseArray,
                cancellationToken: cancellationToken
            );

            // Nothing is read back to check the label is there, because a read can lag behind a write
            return null;
        }
        catch (Exception exception) when (ItemEdit.IsGitHubFailure(exception))
        {
            return GitHubHttpFailure.Describe(exception);
        }
    }

    private async ValueTask<string?> AssignAsync(
        EditRequest request,
        EditState state,
        string login,
        CancellationToken cancellationToken
    )
    {
        if (ItemEdit.IsAssigned(assignees: state.Assignees, login: login))
        {
            return null;
        }

        EditWrite written = await ItemEdit.WriteAsync(
            restClient: this._restClient,
            request: request,
            assignees: ItemEdit.WithAssignee(assignees: state.Assignees, login: login),
            labels: null,
            responseTypeInfo: GitHubJsonContext.Default.PullRequestEditResponse,
            responseNumber: static response => response.Number,
            cancellationToken: cancellationToken
        );

        return written.Outcome == EditWriteOutcome.Edited ? null : written.Reason;
    }

    private static PrSyncLabelsResult Finished(
        EditRequest request,
        SyncProgress progress,
        LabelPlan plan,
        IReadOnlyList<string> added,
        bool assigned
    )
    {
        // A partial read is never reported as a completed sync, even though the labels that were read were added
        return progress.FailedReferences.Count == 0
            ? Result(
                outcome: PrSyncLabelsOutcome.Synced,
                message: $"{request.Reference} synced",
                progress: progress,
                plan: plan,
                added: added,
                assigned: assigned
            )
            : Result(
                outcome: PrSyncLabelsOutcome.Incomplete,
                message: $"the labels of a closing issue of {request.Reference} could not be read, so only those read were added",
                progress: progress,
                plan: plan,
                added: added,
                assigned: assigned
            );
    }

    private static PrSyncLabelsResult Failed(
        SyncProgress progress,
        LabelPlan plan,
        IReadOnlyList<string> added,
        bool assigned,
        string message
    )
    {
        return Result(
            outcome: PrSyncLabelsOutcome.Failed,
            message: message,
            progress: progress,
            plan: plan,
            added: added,
            assigned: assigned
        );
    }

    private static PrSyncLabelsResult Refused(SyncProgress progress, EditRefusal refusal)
    {
        return Result(
            outcome: PrSyncLabelsOutcome.Refused,
            message: $"{refusal.Subject} refused: {refusal.Rule}; nothing was written",
            progress: progress,
            plan: new(Add: [], Skipped: []),
            added: [],
            assigned: false
        ) with
        {
            Rule = refusal.Rule,
        };
    }

    private static PrSyncLabelsResult Result(
        PrSyncLabelsOutcome outcome,
        string message,
        SyncProgress progress,
        LabelPlan plan,
        IReadOnlyList<string> added,
        bool assigned
    )
    {
        return new(
            Outcome: outcome,
            Message: message,
            Url: progress.Url,
            Added: [.. added],
            Skipped: plan.Skipped,
            FailedReferences: progress.FailedReferences,
            Assigned: assigned,
            Rule: null
        );
    }

    private static PrSyncLabelsFailedException Unreadable(EditRequest request, Exception? exception)
    {
        string message = $"could not read the pull request {request.Reference}, so nothing was written";

        return exception is null ? new(message) : new(message: message, innerException: exception);
    }

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("Add={Add.Count}, Skipped={Skipped.Count}")]
    private readonly record struct LabelPlan(IReadOnlyList<string> Add, IReadOnlyList<string> Skipped);

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Url}")]
    private readonly record struct SyncProgress(Uri Url, IReadOnlyList<string> FailedReferences);
}
