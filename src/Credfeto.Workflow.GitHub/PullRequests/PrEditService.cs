using System;
using System.Collections.Generic;
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
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.Leaks;
using Credfeto.Workflow.GitHub.Validation;

namespace Credfeto.Workflow.GitHub.PullRequests;

public sealed partial class PrEditService : IPrEditService
{
    public const string NOTHING_TO_EDIT_MESSAGE = "give at least one of title, body or assignSelf";

    private const string TITLE_SUBJECT = "title";

    private const string BODY_SUBJECT = "body";

    private const string UNEXPECTED_ANSWER = "unexpected answer";

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
            throw Usage(parameterName: nameof(title), message: NOTHING_TO_EDIT_MESSAGE);
        }

        EditRequest request = new(
            Repository: validRepository,
            Number: pullRequest,
            Title: title is null ? null : ValidTitle(title),
            Body: body is null ? null : ValidBody(body),
            AssignSelf: assignSelf
        );

        return this.EditValidAsync(request: request, cancellationToken: cancellationToken);
    }

    private static string ValidTitle(string title)
    {
        if (!TitleValidator.IsValidTitle(title))
        {
            throw Usage(parameterName: nameof(title), message: TitleValidator.INVALID_TITLE_MESSAGE);
        }

        return TitleValidator.IsValidPullRequestTitle(title)
            ? title
            : throw Usage(parameterName: nameof(title), message: TitleValidator.INVALID_PULL_REQUEST_TITLE_MESSAGE);
    }

    // Whitespace and invisible characters alone would show as an empty description on GitHub
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

    private async ValueTask<PrEditResult> EditValidAsync(EditRequest request, CancellationToken cancellationToken)
    {
        PrEditResult? refused = await this.CheckTextAsync(request: request, cancellationToken: cancellationToken);

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

        PullRequestRead read = await this.ReadPullRequestAsync(request: request, cancellationToken: cancellationToken);

        if (read.Result is not null)
        {
            return read.Result;
        }

        PullRequestState state = read.State;

        if (IsUnchanged(request: request, state: state, login: login))
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
            assignees: login is null ? null : WithAssignee(assignees: state.Assignees, login: login),
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<PrEditResult?> CheckTextAsync(EditRequest request, CancellationToken cancellationToken)
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

    private async ValueTask<PullRequestRead> ReadPullRequestAsync(
        EditRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            PullRequestState? state = await this.ReadStateAsync(request: request, cancellationToken: cancellationToken);

            return state is { } found ? new(Result: null, State: found) : Unreadable(request);
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
        catch (Exception exception) when (IsGitHubFailure(exception))
        {
            return Unreadable(request);
        }
    }

    private static PullRequestRead Unreadable(EditRequest request)
    {
        return new(Result: NotEdited(request: request, reason: "could not read the pull request"), State: default);
    }

    private async ValueTask<PullRequestState?> ReadStateAsync(EditRequest request, CancellationToken cancellationToken)
    {
        PullRequestEditResponse response = await this._restClient.GetAsync(
            relativeUri: $"repos/{request.Repository}/pulls/{request.NumberText}",
            responseTypeInfo: GitHubJsonContext.Default.PullRequestEditResponse,
            cancellationToken: cancellationToken
        );

        if (
            response is not { Number: { } number, Title: { } title, Assignees: { } assignees }
            || number != request.Number
            || !CreatedItemUrl.TryParse(
                pattern: PullRequestUrl(),
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

    private static bool IsUnchanged(EditRequest request, PullRequestState state, string? login)
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

    // The issues endpoint is used because it is the one that sets assignees, so all the changes are one write
    private async ValueTask<PrEditResult> WriteAsync(
        EditRequest request,
        PullRequestState state,
        IReadOnlyList<string>? assignees,
        CancellationToken cancellationToken
    )
    {
        PullRequestEditResponse response;

        try
        {
            response = await this._restClient.PatchAsync(
                relativeUri: $"repos/{request.Repository}/issues/{request.NumberText}",
                body: new PullRequestEditRequest(Title: request.Title, Body: request.Body, Assignees: assignees),
                requestTypeInfo: GitHubJsonContext.Default.PullRequestEditRequest,
                responseTypeInfo: GitHubJsonContext.Default.PullRequestEditResponse,
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

        return Result(outcome: PrEditOutcome.Edited, message: $"{request.Reference} was edited") with
        {
            Url = state.Url,
        };
    }

    private static bool IsGitHubFailure(Exception exception)
    {
        return exception is GitHubInvalidResponseException or HttpRequestException;
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
    private static PrEditResult Refused(string subject, string rule, int? lineNumber)
    {
        string line = lineNumber is { } value
            ? $" (line {value.ToString(CultureInfo.InvariantCulture)})"
            : string.Empty;

        return Result(outcome: PrEditOutcome.Refused, message: $"{subject} refused: {rule}{line}") with
        {
            Rule = rule,
            LineNumber = lineNumber,
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
    private readonly record struct PullRequestState(
        Uri Url,
        string Title,
        string? Body,
        IReadOnlyList<string> Assignees
    );

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Result}")]
    private readonly record struct PullRequestRead(PrEditResult? Result, PullRequestState State);
}
