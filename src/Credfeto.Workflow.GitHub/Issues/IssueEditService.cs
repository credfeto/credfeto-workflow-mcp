using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.Leaks;
using Credfeto.Workflow.GitHub.PullRequests;
using Credfeto.Workflow.GitHub.Validation;

namespace Credfeto.Workflow.GitHub.Issues;

public sealed class IssueEditService : IIssueEditService
{
    public const string NOTHING_TO_EDIT_MESSAGE = "give at least one of title, body or assignSelf";

    private const string TITLE_SUBJECT = "title";

    private const string BODY_SUBJECT = "body";

    private const string UNEXPECTED_ANSWER = "unexpected answer";

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
            throw Usage(parameterName: nameof(title), message: NOTHING_TO_EDIT_MESSAGE);
        }

        // GitHubNames.Repository guarantees exactly one separator between a non-empty owner and name
        string owner = validRepository[..validRepository.IndexOf(value: '/', comparisonType: StringComparison.Ordinal)];

        EditRequest request = new(
            Repository: validRepository,
            Number: issue,
            Title: title is null ? null : ValidTitle(owner: owner, title: title),
            Body: body is null ? null : ValidBody(body),
            AssignSelf: assignSelf
        );

        return this.EditValidAsync(request: request, cancellationToken: cancellationToken);
    }

    private static string ValidTitle(string owner, string title)
    {
        if (!TitleValidator.IsValidTitle(title))
        {
            throw Usage(parameterName: nameof(title), message: TitleValidator.INVALID_TITLE_MESSAGE);
        }

        return TitleValidator.IsValidIssueTitle(owner: owner, title: title)
            ? title
            : throw Usage(parameterName: nameof(title), message: TitleValidator.INVALID_ISSUE_TITLE_MESSAGE);
    }

    // Whitespace and invisible characters alone would show as an empty body on GitHub
    private static string ValidBody(string body)
    {
        return TitleValidator.HasVisibleText(body)
            ? body
            : throw Usage(parameterName: nameof(body), message: "the body is empty");
    }

    // The message is fixed text naming the rule the value broke, never the rejected value itself
    private static ParameterValidationException Usage(string parameterName, string message)
    {
        return new(message) { ParameterName = parameterName, Rule = message };
    }

    private async ValueTask<IssueEditResult> EditValidAsync(EditRequest request, CancellationToken cancellationToken)
    {
        IssueEditResult? refused = await this.CheckTextAsync(request: request, cancellationToken: cancellationToken);

        if (refused is not null)
        {
            return refused;
        }

        string? login = null;

        if (request.AssignSelf)
        {
            login = await this.ReadLoginAsync(cancellationToken);

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

        IssueState state = read.State;

        if (IsUnchanged(request: request, state: state, login: login))
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
            assignees: login is null ? null : WithAssignee(assignees: state.Assignees, login: login),
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<IssueEditResult?> CheckTextAsync(EditRequest request, CancellationToken cancellationToken)
    {
        if (request.Title is not null)
        {
            // A title gets the same rules as a body except its length, which the usage checks already limit
            LeakRefusal? titleLeak =
                this._leakChecker.CheckValue(subject: LeakSubject.TITLE, value: request.Title)
                ?? await this._leakChecker.CheckRepositoryReferencesAsync(
                    repository: request.Repository,
                    subject: LeakSubject.TITLE,
                    text: request.Title,
                    cancellationToken: cancellationToken
                );

            if (titleLeak is not null)
            {
                return Refused(subject: TITLE_SUBJECT, rule: LeakRuleNames.Name(titleLeak.Rule), lineNumber: null);
            }
        }

        if (request.Body is null)
        {
            return null;
        }

        LeakRefusal? bodyLeak =
            this._leakChecker.CheckBody(request.Body)
            ?? await this._leakChecker.CheckRepositoryReferencesAsync(
                repository: request.Repository,
                subject: LeakSubject.BODY,
                text: request.Body,
                cancellationToken: cancellationToken
            );

        return bodyLeak is null
            ? null
            : Refused(subject: BODY_SUBJECT, rule: LeakRuleNames.Name(bodyLeak.Rule), lineNumber: bodyLeak.LineNumber);
    }

    private async ValueTask<string?> ReadLoginAsync(CancellationToken cancellationToken)
    {
        try
        {
            IssueUserResponse user = await this._restClient.GetAsync(
                relativeUri: "user",
                responseTypeInfo: GitHubJsonContext.Default.IssueUserResponse,
                cancellationToken: cancellationToken
            );

            return user.Login is { Length: > 0 } login ? login : null;
        }
        catch (Exception exception) when (IsGitHubFailure(exception))
        {
            return null;
        }
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
        catch (Exception exception) when (IsGitHubFailure(exception))
        {
            return Unreadable(request);
        }

        // Pull requests share the issue number space and the issues endpoint answers for them too, but only issues
        // are edited here
        if (response.PullRequest is not null)
        {
            return NotFound(request: request, reason: "it is a pull request");
        }

        return ToState(request: request, response: response) is { } state
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

    private static IssueState? ToState(EditRequest request, IssueEditResponse response)
    {
        if (
            response is not { Number: { } number, Title: { } title, Assignees: { } assignees }
            || number != request.Number
            || !CreatedItemUrl.TryParse(
                pattern: CreatedItemUrl.IssueUrl(),
                url: response.HtmlUrl ?? string.Empty,
                out Uri? url,
                out int urlNumber
            )
            || urlNumber != request.Number
        )
        {
            return null;
        }

        // An assignee that cannot be named would be dropped by the write, so such an answer is not trusted
        string?[] logins = [.. assignees.Select(static assignee => assignee?.Login)];

        return logins.All(static login => login is { Length: > 0 })
            ? new(Url: url, Title: title, Body: response.Body, Assignees: [.. logins.OfType<string>()])
            : null;
    }

    private static bool IsUnchanged(EditRequest request, IssueState state, string? login)
    {
        return (request.Title is null || StringComparer.Ordinal.Equals(x: request.Title, y: state.Title))
            && (request.Body is null || StringComparer.Ordinal.Equals(x: request.Body, y: state.Body))
            && (login is null || IsAssigned(assignees: state.Assignees, login: login));
    }

    // Logins are not case-sensitive on GitHub
    private static bool IsAssigned(IReadOnlyList<string> assignees, string login)
    {
        return assignees.Contains(value: login, comparer: StringComparer.OrdinalIgnoreCase);
    }

    // GitHub replaces the assignees with the list sent, so the current ones are kept
    private static IReadOnlyList<string> WithAssignee(IReadOnlyList<string> assignees, string login)
    {
        return IsAssigned(assignees: assignees, login: login) ? assignees : [.. assignees, login];
    }

    private async ValueTask<IssueEditResult> WriteAsync(
        EditRequest request,
        IssueState state,
        IReadOnlyList<string>? assignees,
        CancellationToken cancellationToken
    )
    {
        IssueEditResponse response;

        try
        {
            response = await this._restClient.PatchAsync(
                relativeUri: $"repos/{request.Repository}/issues/{request.NumberText}",
                body: new PullRequestEditRequest(Title: request.Title, Body: request.Body, Assignees: assignees),
                requestTypeInfo: GitHubJsonContext.Default.PullRequestEditRequest,
                responseTypeInfo: GitHubJsonContext.Default.IssueEditResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (GitHubHttpFailure.IsRejected(exception.StatusCode))
        {
            return NotEdited(request: request, reason: GitHubHttpFailure.Describe(exception));
        }
        catch (Exception exception) when (IsGitHubFailure(exception))
        {
            // The request was sent and never retried, so GitHub may have made the change without the answer arriving
            return MayHaveBeenEdited(request: request, reason: GitHubHttpFailure.Describe(exception));
        }

        if (response.Number != request.Number)
        {
            return MayHaveBeenEdited(request: request, reason: UNEXPECTED_ANSWER);
        }

        return Result(outcome: IssueEditOutcome.Edited, message: $"{request.Reference} was edited") with
        {
            Url = state.Url,
        };
    }

    private static bool IsGitHubFailure(Exception exception)
    {
        return exception is GitHubInvalidResponseException or HttpRequestException;
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
    private static IssueEditResult Refused(string subject, string rule, int? lineNumber)
    {
        string line = lineNumber is { } value
            ? $" (line {value.ToString(CultureInfo.InvariantCulture)})"
            : string.Empty;

        return Result(outcome: IssueEditOutcome.Refused, message: $"{subject} refused: {rule}{line}") with
        {
            Rule = rule,
            LineNumber = lineNumber,
        };
    }

    private static IssueEditResult Result(IssueEditOutcome outcome, string message)
    {
        return new(Outcome: outcome, Message: message, Url: null, Rule: null, LineNumber: null);
    }

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Repository}#{Number}")]
    private readonly record struct EditRequest(
        string Repository,
        int Number,
        string? Title,
        string? Body,
        bool AssignSelf
    )
    {
        public string NumberText => this.Number.ToString(CultureInfo.InvariantCulture);

        public string Reference => $"{this.Repository}#{this.NumberText}";
    }

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Url}: {Title}")]
    private readonly record struct IssueState(Uri Url, string Title, string? Body, IReadOnlyList<string> Assignees);

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Result}")]
    private readonly record struct IssueRead(IssueEditResult? Result, IssueState State);
}
