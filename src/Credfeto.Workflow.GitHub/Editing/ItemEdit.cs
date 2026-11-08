using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.Leaks;
using Credfeto.Workflow.GitHub.PullRequests;
using Credfeto.Workflow.GitHub.Validation;

namespace Credfeto.Workflow.GitHub.Editing;

// Issues and pull requests share the issues endpoint for their writes, so everything an edit does apart from reading
// the item and wording its result is the same for both
internal static class ItemEdit
{
    public const string NOTHING_TO_EDIT_MESSAGE = "give at least one of title, body or assignSelf";

    private const string TITLE_SUBJECT = "title";

    private const string BODY_SUBJECT = "body";

    private const string UNEXPECTED_ANSWER = "unexpected answer";

    // Whitespace and invisible characters alone would show as an empty body on GitHub
    public static string ValidBody(string body)
    {
        return TitleValidator.HasVisibleText(body)
            ? body
            : throw Usage(parameterName: nameof(body), message: "the body is empty");
    }

    // The message is fixed text naming the rule the value broke, never the rejected value itself
    public static ParameterValidationException Usage(string parameterName, string message)
    {
        return new(message) { ParameterName = parameterName, Rule = message };
    }

    public static async ValueTask<EditRefusal?> CheckTextAsync(
        ILeakChecker leakChecker,
        EditRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.Title is not null)
        {
            // A title gets the same rules as a body except its length, which the usage checks already limit
            LeakRefusal? titleLeak =
                leakChecker.CheckValue(subject: LeakSubject.TITLE, value: request.Title)
                ?? await leakChecker.CheckRepositoryReferencesAsync(
                    repository: request.Repository,
                    subject: LeakSubject.TITLE,
                    text: request.Title,
                    cancellationToken: cancellationToken
                );

            if (titleLeak is not null)
            {
                return new(Subject: TITLE_SUBJECT, Rule: LeakRuleNames.Name(titleLeak.Rule), LineNumber: null);
            }
        }

        if (request.Body is null)
        {
            return null;
        }

        LeakRefusal? bodyLeak =
            leakChecker.CheckBody(request.Body)
            ?? await leakChecker.CheckRepositoryReferencesAsync(
                repository: request.Repository,
                subject: LeakSubject.BODY,
                text: request.Body,
                cancellationToken: cancellationToken
            );

        return bodyLeak is null
            ? null
            : new EditRefusal(
                Subject: BODY_SUBJECT,
                Rule: LeakRuleNames.Name(bodyLeak.Rule),
                LineNumber: bodyLeak.LineNumber
            );
    }

    public static async ValueTask<string?> ReadLoginAsync(
        IGitHubRestClient restClient,
        CancellationToken cancellationToken
    )
    {
        try
        {
            IssueUserResponse user = await restClient.GetAsync(
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

    public static EditState? ToState(
        EditRequest request,
        Regex urlPattern,
        int? number,
        string? htmlUrl,
        string? title,
        string? body,
        IEnumerable<string?>? assigneeLogins
    )
    {
        if (
            number != request.Number
            || title is null
            || assigneeLogins is null
            || !CreatedItemUrl.TryParse(
                pattern: urlPattern,
                url: htmlUrl ?? string.Empty,
                out Uri? url,
                out int urlNumber
            )
            || urlNumber != request.Number
        )
        {
            return null;
        }

        // An assignee that cannot be named would be dropped by the write, so such an answer is not trusted
        string?[] logins = [.. assigneeLogins];

        return logins.All(static login => login is { Length: > 0 })
            ? new(Url: url, Title: title, Body: body, Assignees: [.. logins.OfType<string>()])
            : null;
    }

    public static bool IsUnchanged(EditRequest request, EditState state, string? login)
    {
        return (request.Title is null || StringComparer.Ordinal.Equals(x: request.Title, y: state.Title))
            && (request.Body is null || StringComparer.Ordinal.Equals(x: request.Body, y: state.Body))
            && (login is null || IsAssigned(assignees: state.Assignees, login: login));
    }

    // GitHub replaces the assignees with the list sent, so the current ones are kept
    public static IReadOnlyList<string> WithAssignee(IReadOnlyList<string> assignees, string login)
    {
        return IsAssigned(assignees: assignees, login: login) ? assignees : [.. assignees, login];
    }

    // Logins are not case-sensitive on GitHub
    private static bool IsAssigned(IReadOnlyList<string> assignees, string login)
    {
        return assignees.Contains(value: login, comparer: StringComparer.OrdinalIgnoreCase);
    }

    // The issues endpoint is used for pull requests too because it is the one that sets assignees, so all the changes
    // are one write
    public static async ValueTask<EditWrite> WriteAsync<TResponse>(
        IGitHubRestClient restClient,
        EditRequest request,
        IReadOnlyList<string>? assignees,
        JsonTypeInfo<TResponse> responseTypeInfo,
        Func<TResponse, int?> responseNumber,
        CancellationToken cancellationToken
    )
    {
        TResponse response;

        try
        {
            response = await restClient.PatchAsync(
                relativeUri: $"repos/{request.Repository}/issues/{request.NumberText}",
                body: new PullRequestEditRequest(Title: request.Title, Body: request.Body, Assignees: assignees),
                requestTypeInfo: GitHubJsonContext.Default.PullRequestEditRequest,
                responseTypeInfo: responseTypeInfo,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (GitHubHttpFailure.IsRejected(exception.StatusCode))
        {
            return new(Outcome: EditWriteOutcome.Rejected, Reason: GitHubHttpFailure.Describe(exception));
        }
        catch (Exception exception) when (IsGitHubFailure(exception))
        {
            // The request was sent and never retried, so GitHub may have made the change without the answer arriving
            return new(Outcome: EditWriteOutcome.MayHaveBeenEdited, Reason: GitHubHttpFailure.Describe(exception));
        }

        return responseNumber(response) == request.Number
            ? new(Outcome: EditWriteOutcome.Edited, Reason: string.Empty)
            : new(Outcome: EditWriteOutcome.MayHaveBeenEdited, Reason: UNEXPECTED_ANSWER);
    }

    public static bool IsGitHubFailure(Exception exception)
    {
        return exception is GitHubInvalidResponseException or HttpRequestException;
    }
}
