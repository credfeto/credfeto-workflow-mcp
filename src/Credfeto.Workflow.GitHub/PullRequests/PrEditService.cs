using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Editing;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.Validation;

namespace Credfeto.Workflow.GitHub.PullRequests;

public sealed partial class PrEditService : IPrEditService
{
    public const string NOTHING_TO_EDIT_MESSAGE = ItemEdit.NOTHING_TO_EDIT_MESSAGE;

    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    private readonly ILeakChecker _leakChecker;

    private readonly IGitHubRestClient _restClient;

    public PrEditService(IGitHubRestClient restClient, ILeakChecker leakChecker)
    {
        this._restClient = restClient;
        this._leakChecker = leakChecker;
    }

    public ValueTask<PrEditResult> EditAsync(
        string repository,
        int pullRequest,
        string? title,
        string? body,
        bool assignSelf,
        CancellationToken cancellationToken
    )
    {
        // Every argument is validated before the first GitHub call, so invalid input never costs one and is reported
        // as such rather than as a GitHub failure
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (pullRequest <= 0)
        {
            throw ParameterValidationException.For(parameterName: "pr", rule: "must be above 0");
        }

        if (title is null && body is null && !assignSelf)
        {
            throw ItemEdit.Usage(parameterName: nameof(title), message: NOTHING_TO_EDIT_MESSAGE);
        }

        EditRequest request = new(
            Repository: validRepository,
            Number: pullRequest,
            Title: title is null ? null : ValidTitle(title),
            Body: body is null ? null : ItemEdit.ValidBody(body),
            AssignSelf: assignSelf
        );

        return this.EditValidAsync(request: request, cancellationToken: cancellationToken);
    }

    private static string ValidTitle(string title)
    {
        if (!TitleValidator.IsValidTitle(title))
        {
            throw ItemEdit.Usage(parameterName: nameof(title), message: TitleValidator.INVALID_TITLE_MESSAGE);
        }

        return TitleValidator.IsValidPullRequestTitle(title)
            ? title
            : throw ItemEdit.Usage(
                parameterName: nameof(title),
                message: TitleValidator.INVALID_PULL_REQUEST_TITLE_MESSAGE
            );
    }

    private async ValueTask<PrEditResult> EditValidAsync(EditRequest request, CancellationToken cancellationToken)
    {
        EditRefusal? refusal = await ItemEdit.CheckTextAsync(
            leakChecker: this._leakChecker,
            request: request,
            cancellationToken: cancellationToken
        );

        if (refusal is { } refused)
        {
            return Refused(refused);
        }

        string? login = null;

        if (request.AssignSelf)
        {
            login = await ItemEdit.ReadLoginAsync(restClient: this._restClient, cancellationToken: cancellationToken);

            if (login is null)
            {
                return NotEdited(request: request, reason: "could not read the signed-in account");
            }
        }

        PullRequestRead read = await this.ReadPullRequestAsync(request: request, cancellationToken: cancellationToken);

        if (read.Result is not null)
        {
            return read.Result;
        }

        EditState state = read.State;

        if (ItemEdit.IsUnchanged(request: request, state: state, login: login))
        {
            return Result(
                outcome: PrEditOutcome.Unchanged,
                message: $"{request.Reference} already is as asked, so nothing was written"
            ) with
            {
                Url = state.Url,
            };
        }

        return await this.WriteAsync(
            request: request,
            state: state,
            login: login,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<PrEditResult> WriteAsync(
        EditRequest request,
        EditState state,
        string? login,
        CancellationToken cancellationToken
    )
    {
        EditWrite written = await ItemEdit.WriteAsync(
            restClient: this._restClient,
            request: request,
            assignees: login is null ? null : ItemEdit.WithAssignee(assignees: state.Assignees, login: login),
            responseTypeInfo: GitHubJsonContext.Default.PullRequestEditResponse,
            responseNumber: static response => response.Number,
            cancellationToken: cancellationToken
        );

        return written.Outcome switch
        {
            EditWriteOutcome.Edited => Result(
                outcome: PrEditOutcome.Edited,
                message: $"{request.Reference} was edited"
            ) with
            {
                Url = state.Url,
            },
            EditWriteOutcome.Rejected => NotEdited(request: request, reason: written.Reason),
            _ => MayHaveBeenEdited(request: request, reason: written.Reason),
        };
    }

    private async ValueTask<PullRequestRead> ReadPullRequestAsync(
        EditRequest request,
        CancellationToken cancellationToken
    )
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
            // An issue shares the pull request number space, and GitHub answers 404 for one asked for as a pull request
            return new(
                Result: Result(
                    outcome: PrEditOutcome.NotFound,
                    message: $"the pull request {request.Reference} was not found, or the caller cannot see it"
                ),
                State: default
            );
        }
        catch (Exception exception) when (ItemEdit.IsGitHubFailure(exception))
        {
            return Unreadable(request);
        }

        return
            ItemEdit.ToState(
                request: request,
                urlPattern: PullRequestUrl(),
                number: response.Number,
                htmlUrl: response.HtmlUrl,
                title: response.Title,
                body: response.Body,
                assigneeLogins: response.Assignees?.Select(static assignee => assignee?.Login)
            )
                is { } state
            ? new(Result: null, State: state)
            : Unreadable(request);
    }

    private static PullRequestRead Unreadable(EditRequest request)
    {
        return new(Result: NotEdited(request: request, reason: "could not read the pull request"), State: default);
    }

    private static PrEditResult NotEdited(EditRequest request, string reason)
    {
        return Result(outcome: PrEditOutcome.NotEdited, message: $"{request.Reference} was not edited: {reason}");
    }

    private static PrEditResult MayHaveBeenEdited(EditRequest request, string reason)
    {
        return Result(
            outcome: PrEditOutcome.MayHaveBeenEdited,
            message: $"{request.Reference} may have been edited ({reason}); read the pull request before trying again"
        );
    }

    // The refused text is never echoed back: only the subject, the rule and, for a body, the line are reported
    private static PrEditResult Refused(EditRefusal refusal)
    {
        string line = refusal.LineNumber is { } value
            ? $" (line {value.ToString(CultureInfo.InvariantCulture)})"
            : string.Empty;

        return Result(outcome: PrEditOutcome.Refused, message: $"{refusal.Subject} refused: {refusal.Rule}{line}") with
        {
            Rule = refusal.Rule,
            LineNumber = refusal.LineNumber,
        };
    }

    private static PrEditResult Result(PrEditOutcome outcome, string message)
    {
        return new(Outcome: outcome, Message: message, Url: null, Rule: null, LineNumber: null);
    }

    [GeneratedRegex(
        pattern: @"^https://[A-Za-z0-9.-]+(:[0-9]+)?/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/pull/(?<number>[0-9]+)\z",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex PullRequestUrl();

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Result}")]
    private readonly record struct PullRequestRead(PrEditResult? Result, EditState State);
}
