using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.BodyRules;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Issues;

public sealed partial class IssueService : IIssueService
{
    private const string OPEN = "OPEN";

    private const string CLOSED = "CLOSED";

    private const string APP_PREFIX = "app/";

    private const int MAX_COMMENTS_READ = 10000;

    private const string REASON_COMPLETED = "completed";

    private const string REASON_NOT_PLANNED = "not_planned";

    private const string REASON_NOT_PLANNED_INPUT = "not-planned";

    private const string OUTCOME_CLOSED = "closed";

    private const string OUTCOME_ALREADY_CLOSED = "already-closed";

    private const string ON_HOLD_RULE = "an issue labelled On Hold may only be closed by a human";

    private const string CLOSING_PULL_REQUEST_RULE = "an open pull request will close the issue when it merges";

    private const string APPROVAL_KEYWORD_RULE = "the comment must not contain an approval keyword";

    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    private readonly ILeakChecker _leakChecker;

    private readonly IGitHubRestClient _restClient;

    public IssueService(IGitHubRestClient restClient, ILeakChecker leakChecker)
    {
        this._restClient = restClient;
        this._leakChecker = leakChecker;
    }

    public async ValueTask<IssueViewResponse> ViewAsync(
        string repository,
        int issue,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (issue <= 0)
        {
            throw ParameterValidationException.For(parameterName: nameof(issue), rule: "must be above 0");
        }

        string reference = $"{validRepository}#{issue}";

        try
        {
            IssueResponse response = await this._restClient.GetAsync(
                relativeUri: $"repos/{validRepository}/issues/{issue}",
                responseTypeInfo: GitHubJsonContext.Default.IssueResponse,
                cancellationToken: cancellationToken
            );

            return Project(reference: reference, issue: issue, response: response);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new IssueNotFoundException(message: NotFoundMessage(reference), innerException: exception);
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new IssueViewFailedException(message: $"could not read issue {reference}", innerException: exception);
        }
    }

    public async ValueTask<IssueCloseResponse> CloseAsync(
        string repository,
        int issue,
        string reason,
        string? comment,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (issue <= 0)
        {
            throw ParameterValidationException.For(parameterName: nameof(issue), rule: "must be above 0");
        }

        string validReason = CloseReason(reason);
        string? validComment = string.IsNullOrWhiteSpace(comment) ? null : this.CheckComment(comment);
        string reference = $"{validRepository}#{issue}";
        string issueUri = $"repos/{validRepository}/issues/{issue.ToString(CultureInfo.InvariantCulture)}";

        IssueResponse current = await this.ReadForCloseAsync(
            issueUri: issueUri,
            reference: reference,
            issue: issue,
            cancellationToken: cancellationToken
        );

        if (StringComparer.Ordinal.Equals(x: State(current.State), y: CLOSED))
        {
            return new(
                Issue: reference,
                Outcome: OUTCOME_ALREADY_CLOSED,
                Reason: validReason,
                Url: IssueUrl(current.HtmlUrl)
            );
        }

        if (Labels(current.Labels).Contains(value: StandardLabels.ON_HOLD, comparer: StringComparer.OrdinalIgnoreCase))
        {
            throw new IssueCloseRefusedException($"closing issue {reference} was refused") { Rule = ON_HOLD_RULE };
        }

        return await this.CloseOpenIssueAsync(
            validRepository: validRepository,
            issueUri: issueUri,
            reference: reference,
            issue: issue,
            reason: validReason,
            comment: validComment,
            cancellationToken: cancellationToken
        );
    }

    public async ValueTask<IssueComments> CommentsAsync(
        string repository,
        int issue,
        string? since,
        string? author,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (issue <= 0)
        {
            throw ParameterValidationException.For(parameterName: nameof(issue), rule: "must be above 0");
        }

        DateTime? validSince = Since(since);
        string? validAuthor = Author(author);
        string reference = $"{validRepository}#{issue}";

        try
        {
            IssueResponse response = await this._restClient.GetAsync(
                relativeUri: $"repos/{validRepository}/issues/{issue}",
                responseTypeInfo: GitHubJsonContext.Default.IssueResponse,
                cancellationToken: cancellationToken
            );

            Uri url = IssueUrl(response.HtmlUrl);
            int expected = ExpectedCommentCount(reference: reference, response: response);

            IReadOnlyList<IssueCommentResponse?> comments = await this._restClient.GetAllPagesAsync(
                relativeUri: $"repos/{validRepository}/issues/{issue}/comments?per_page=100",
                pageTypeInfo: GitHubJsonContext.Default.IssueCommentResponseArray,
                cancellationToken: cancellationToken
            );

            // A comment added or deleted between the two reads would leave a partial list, which is refused
            if (comments.Count != expected)
            {
                throw IssueCommentsFailure.Incomplete(reference);
            }

            return new IssueCommentsProjector(reference).Project(
                url: url,
                responses: comments,
                since: validSince,
                author: validAuthor
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new IssueNotFoundException(message: NotFoundMessage(reference), innerException: exception);
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new IssueViewFailedException(message: $"could not read issue {reference}", innerException: exception);
        }
    }

    private static DateTime? Since(string? since)
    {
        if (since is null)
        {
            return null;
        }

        if (!IssueCommentsProjector.TryParseTimestamp(value: since, out DateTime timestamp))
        {
            throw ParameterValidationException.For(
                parameterName: nameof(since),
                rule: "it must be a UTC time in the form yyyy-MM-ddTHH:mm:ssZ"
            );
        }

        return timestamp;
    }

    private static string? Author(string? author)
    {
        if (author is null)
        {
            return null;
        }

        bool isApp = author.StartsWith(value: APP_PREFIX, comparisonType: StringComparison.Ordinal);
        string name = GitHubNames.Login(
            value: isApp ? author[APP_PREFIX.Length..] : author,
            parameterName: nameof(author)
        );

        // A bot is printed as app/<slug>, so a '[bot]' login or a name GitHub cannot issue would never match
        if (
            name.EndsWith(value: IssueCommentsProjector.BOT_SUFFIX, comparisonType: StringComparison.Ordinal)
            || name[0] == '-'
        )
        {
            throw ParameterValidationException.For(
                parameterName: nameof(author),
                rule: "it must be a login, or app/<slug> for a GitHub App, without '[bot]' or a leading '-'"
            );
        }

        return author;
    }

    private static Uri IssueUrl(string? htmlUrl)
    {
        if (
            htmlUrl is null
            || !Uri.TryCreate(uriString: htmlUrl, uriKind: UriKind.Absolute, out Uri? uri)
            || !UriSchemes.IsHttp(uri)
        )
        {
            throw new GitHubInvalidResponseException("GitHub returned an issue without a valid link");
        }

        return uri;
    }

    private static int ExpectedCommentCount(string reference, IssueResponse response)
    {
        int count =
            response.Comments
            ?? throw new GitHubInvalidResponseException("GitHub returned an issue without a comment count");

        return count is >= 0 and <= MAX_COMMENTS_READ ? count : throw IssueCommentsFailure.Incomplete(reference);
    }

    private static string NotFoundMessage(string reference)
    {
        return $"issue {reference} was not found";
    }

    private static IssueViewResponse Project(string reference, int issue, IssueResponse response)
    {
        // GitHub serves pull requests from the issues endpoint too, so one is reported as no such issue
        if (IsPullRequest(issue: issue, response: response))
        {
            throw new IssueNotFoundException(NotFoundMessage(reference));
        }

        string title =
            response.Title ?? throw new GitHubInvalidResponseException("GitHub returned an issue without a title");

        return new(
            Issue: reference,
            Title: UpstreamText.ReplaceLineBreaking(title),
            State: State(response.State),
            Labels: Labels(response.Labels),
            Assignees: Assignees(response.Assignees),
            Body: response.Body ?? string.Empty
        );
    }

    private static bool IsPullRequest(int issue, IssueResponse response)
    {
        return response.PullRequest is not null
            || response.HtmlUrl is { } htmlUrl
                && htmlUrl.EndsWith(value: $"/pull/{issue}", comparisonType: StringComparison.Ordinal);
    }

    private static string State(string? state)
    {
        return state switch
        {
            "open" => OPEN,
            "closed" => CLOSED,
            _ => throw new GitHubInvalidResponseException("GitHub returned an issue with an unknown state"),
        };
    }

    private static IReadOnlyList<string> Labels(IReadOnlyList<IssueLabelResponse?>? labels)
    {
        return
        [
            .. (labels ?? []).Select(label =>
                UpstreamText.ReplaceLineBreaking(
                    label?.Name ?? throw new GitHubInvalidResponseException("GitHub returned a label without a name")
                )
            ),
        ];
    }

    private static IReadOnlyList<string> Assignees(IReadOnlyList<IssueUserResponse?>? assignees)
    {
        return
        [
            .. (assignees ?? []).Select(assignee =>
                UpstreamText.ReplaceLineBreaking(
                    assignee?.Login
                        ?? throw new GitHubInvalidResponseException("GitHub returned an assignee without a login")
                )
            ),
        ];
    }

    private async ValueTask<IssueCloseResponse> CloseOpenIssueAsync(
        string validRepository,
        string issueUri,
        string reference,
        int issue,
        string reason,
        string? comment,
        CancellationToken cancellationToken
    )
    {
        try
        {
            IReadOnlyList<IssueTimelineEventResponse> timeline = await this._restClient.GetAllPagesAsync(
                relativeUri: $"{issueUri}/timeline?per_page=100",
                pageTypeInfo: GitHubJsonContext.Default.IssueTimelineEventResponseArray,
                cancellationToken: cancellationToken
            );

            if (HasOpenClosingPullRequest(timeline: timeline, validRepository: validRepository, issue: issue))
            {
                throw new IssueCloseRefusedException($"closing issue {reference} was refused")
                {
                    Rule = CLOSING_PULL_REQUEST_RULE,
                };
            }

            if (comment is not null)
            {
                await this._restClient.PostAsync(
                    relativeUri: $"{issueUri}/comments",
                    body: new IssueCommentCreateRequest(Body: comment),
                    requestTypeInfo: GitHubJsonContext.Default.IssueCommentCreateRequest,
                    responseTypeInfo: GitHubJsonContext.Default.IssueCommentResponse,
                    cancellationToken: cancellationToken
                );
            }

            IssueResponse closed = await this._restClient.PatchAsync(
                relativeUri: issueUri,
                body: new IssueCloseRequest(State: "closed", StateReason: reason),
                requestTypeInfo: GitHubJsonContext.Default.IssueCloseRequest,
                responseTypeInfo: GitHubJsonContext.Default.IssueResponse,
                cancellationToken: cancellationToken
            );

            return new(Issue: reference, Outcome: OUTCOME_CLOSED, Reason: reason, Url: IssueUrl(closed.HtmlUrl));
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new IssueCloseFailedException(
                message: $"could not close issue {reference}",
                innerException: exception
            );
        }
    }

    private async ValueTask<IssueResponse> ReadForCloseAsync(
        string issueUri,
        string reference,
        int issue,
        CancellationToken cancellationToken
    )
    {
        IssueResponse response;

        try
        {
            response = await this._restClient.GetAsync(
                relativeUri: issueUri,
                responseTypeInfo: GitHubJsonContext.Default.IssueResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new IssueNotFoundException(message: NotFoundMessage(reference), innerException: exception);
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new IssueCloseFailedException(
                message: $"could not close issue {reference}",
                innerException: exception
            );
        }

        // GitHub serves pull requests from the issues endpoint too, so one is reported as no such issue
        return IsPullRequest(issue: issue, response: response)
            ? throw new IssueNotFoundException(NotFoundMessage(reference))
            : response;
    }

    private static string CloseReason(string reason)
    {
        return reason switch
        {
            REASON_COMPLETED => REASON_COMPLETED,
            REASON_NOT_PLANNED or REASON_NOT_PLANNED_INPUT => REASON_NOT_PLANNED,
            _ => throw ParameterValidationException.For(
                parameterName: nameof(reason),
                rule: "must be completed or not-planned"
            ),
        };
    }

    private string CheckComment(string comment)
    {
        // The comment text is never echoed back: only the rule and line number are reported
        if (this._leakChecker.CheckBody(comment) is { } leak)
        {
            throw new IssueCloseRefusedException("the closing comment was refused")
            {
                Rule = $"the comment must not contain {leak.Rule}",
                LineNumber = leak.LineNumber,
            };
        }

        if (ApprovalKeywordMatcher.FindFirstKeywordLine(comment) is { } approval)
        {
            throw new IssueCloseRefusedException("the closing comment was refused")
            {
                Rule = APPROVAL_KEYWORD_RULE,
                LineNumber = approval.LineNumber,
            };
        }

        return comment;
    }

    private static bool HasOpenClosingPullRequest(
        IReadOnlyList<IssueTimelineEventResponse> timeline,
        string validRepository,
        int issue
    )
    {
        return timeline.Any(timelineEvent =>
            StringComparer.Ordinal.Equals(x: timelineEvent.Event, y: "cross-referenced")
            && timelineEvent.Source?.Issue is { PullRequest: not null } source
            && StringComparer.Ordinal.Equals(x: source.State, y: "open")
            && ClosesIssue(source: source, validRepository: validRepository, issue: issue)
        );
    }

    private static bool ClosesIssue(IssueTimelineSourceIssueResponse source, string validRepository, int issue)
    {
        if (string.IsNullOrEmpty(source.Body))
        {
            return false;
        }

        string? sourceRepository = SourceRepository(source.RepositoryUrl);
        string expectedNumber = issue.ToString(CultureInfo.InvariantCulture);

        return ClosingKeyword()
            .Matches(source.Body)
            .Any(match =>
                StringComparer.Ordinal.Equals(x: match.Groups["number"].Value, y: expectedNumber)
                && StringComparer.OrdinalIgnoreCase.Equals(
                    x: match.Groups["repo"].Success ? match.Groups["repo"].Value : sourceRepository,
                    y: validRepository
                )
            );
    }

    private static string? SourceRepository(string? repositoryUrl)
    {
        const string marker = "/repos/";

        if (string.IsNullOrEmpty(repositoryUrl))
        {
            return null;
        }

        int index = repositoryUrl.IndexOf(value: marker, comparisonType: StringComparison.Ordinal);

        return index < 0 ? null : repositoryUrl[(index + marker.Length)..];
    }

    [GeneratedRegex(
        pattern: @"\b(close|closes|closed|fix|fixes|fixed|resolve|resolves|resolved)[\s:]+(?<repo>[\w.-]+/[\w.-]+)?#(?<number>\d+)\b",
        options: RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex ClosingKeyword();
}
