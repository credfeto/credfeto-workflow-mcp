using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Editing;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.Validation;

namespace Credfeto.Workflow.GitHub.Issues;

public sealed class IssueEditService : IIssueEditService
{
    public const string NOTHING_TO_EDIT_MESSAGE = ItemEdit.NOTHING_TO_EDIT_MESSAGE;

    private readonly ILeakChecker _leakChecker;

    private readonly IGitHubRestClient _restClient;

    public IssueEditService(IGitHubRestClient restClient, ILeakChecker leakChecker)
    {
        this._restClient = restClient;
        this._leakChecker = leakChecker;
    }

    public ValueTask<IssueEditResult> EditAsync(
        string repository,
        int issue,
        string? title,
        string? body,
        bool assignSelf,
        CancellationToken cancellationToken
    )
    {
        // Every argument is validated before the first GitHub call, so invalid input never costs one and is reported
        // as such rather than as a GitHub failure
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (issue <= 0)
        {
            throw ParameterValidationException.For(parameterName: nameof(issue), rule: "must be above 0");
        }

        if (title is null && body is null && !assignSelf)
        {
            throw ItemEdit.Usage(parameterName: nameof(title), message: NOTHING_TO_EDIT_MESSAGE);
        }

        // GitHubNames.Repository guarantees exactly one separator between a non-empty owner and name
        string owner = validRepository[..validRepository.IndexOf(value: '/', comparisonType: StringComparison.Ordinal)];

        EditRequest request = new(
            Repository: validRepository,
            Number: issue,
            Title: title is null ? null : ValidTitle(owner: owner, title: title),
            Body: body is null ? null : ItemEdit.ValidBody(body),
            AssignSelf: assignSelf
        );

        return this.EditValidAsync(request: request, cancellationToken: cancellationToken);
    }

    private static string ValidTitle(string owner, string title)
    {
        if (!TitleValidator.IsValidTitle(title))
        {
            throw ItemEdit.Usage(parameterName: nameof(title), message: TitleValidator.INVALID_TITLE_MESSAGE);
        }

        return TitleValidator.IsValidIssueTitle(owner: owner, title: title)
            ? title
            : throw ItemEdit.Usage(parameterName: nameof(title), message: TitleValidator.INVALID_ISSUE_TITLE_MESSAGE);
    }

    private async ValueTask<IssueEditResult> EditValidAsync(EditRequest request, CancellationToken cancellationToken)
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

        IssueRead read = await this.ReadIssueAsync(request: request, cancellationToken: cancellationToken);

        if (read.Result is not null)
        {
            return read.Result;
        }

        EditState state = read.State;

        if (ItemEdit.IsUnchanged(request: request, state: state, login: login))
        {
            return Result(
                outcome: IssueEditOutcome.Unchanged,
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

    private async ValueTask<IssueEditResult> WriteAsync(
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
            responseTypeInfo: GitHubJsonContext.Default.IssueEditResponse,
            responseNumber: static response => response.Number,
            cancellationToken: cancellationToken
        );

        return written.Outcome switch
        {
            EditWriteOutcome.Edited => Result(
                outcome: IssueEditOutcome.Edited,
                message: $"{request.Reference} was edited"
            ) with
            {
                Url = state.Url,
            },
            EditWriteOutcome.Rejected => NotEdited(request: request, reason: written.Reason),
            _ => MayHaveBeenEdited(request: request, reason: written.Reason),
        };
    }

    private async ValueTask<IssueRead> ReadIssueAsync(EditRequest request, CancellationToken cancellationToken)
    {
        IssueEditResponse response;

        try
        {
            response = await this._restClient.GetAsync(
                relativeUri: $"repos/{request.Repository}/issues/{request.NumberText}",
                responseTypeInfo: GitHubJsonContext.Default.IssueEditResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return NotFound(request: request, reason: "or the caller cannot see it");
        }
        catch (Exception exception) when (ItemEdit.IsGitHubFailure(exception))
        {
            return Unreadable(request);
        }

        // Pull requests share the issue number space and the issues endpoint answers for them too, but only issues
        // are edited here
        if (response.PullRequest is not null)
        {
            return NotFound(request: request, reason: "it is a pull request");
        }

        return
            ItemEdit.ToState(
                request: request,
                urlPattern: CreatedItemUrl.IssueUrl(),
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

    private static IssueRead NotFound(EditRequest request, string reason)
    {
        return new(
            Result: Result(
                outcome: IssueEditOutcome.NotFound,
                message: $"the issue {request.Reference} was not found, {reason}"
            ),
            State: default
        );
    }

    private static IssueRead Unreadable(EditRequest request)
    {
        return new(Result: NotEdited(request: request, reason: "could not read the issue"), State: default);
    }

    private static IssueEditResult NotEdited(EditRequest request, string reason)
    {
        return Result(outcome: IssueEditOutcome.Failed, message: $"{request.Reference} was not edited: {reason}");
    }

    private static IssueEditResult MayHaveBeenEdited(EditRequest request, string reason)
    {
        return Result(
            outcome: IssueEditOutcome.Failed,
            message: $"{request.Reference} may have been edited ({reason}); read the issue before trying again"
        );
    }

    // The refused text is never echoed back: only the subject, the rule and, for a body, the line are reported
    private static IssueEditResult Refused(EditRefusal refusal)
    {
        string line = refusal.LineNumber is { } value
            ? $" (line {value.ToString(CultureInfo.InvariantCulture)})"
            : string.Empty;

        return Result(
            outcome: IssueEditOutcome.Refused,
            message: $"{refusal.Subject} refused: {refusal.Rule}{line}"
        ) with
        {
            Rule = refusal.Rule,
            LineNumber = refusal.LineNumber,
        };
    }

    private static IssueEditResult Result(IssueEditOutcome outcome, string message)
    {
        return new(Outcome: outcome, Message: message, Url: null, Rule: null, LineNumber: null);
    }

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Result}")]
    private readonly record struct IssueRead(IssueEditResult? Result, EditState State);
}
