using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.PullRequests;

public sealed partial class PrReplyService : IPrReplyService
{
    private const string READ_FAILED = "the reply was not posted: could not read the comment";

    private const string UNEXPECTED_ANSWER = "unexpected answer";

    private const string PULLS_SEGMENT = "/pulls/";

    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    private readonly ILeakChecker _leakChecker;

    private readonly IGitHubRestClient _restClient;

    public PrReplyService(IGitHubRestClient restClient, ILeakChecker leakChecker)
    {
        this._restClient = restClient;
        this._leakChecker = leakChecker;
    }

    public async ValueTask<PrReplyResult> ReplyAsync(
        string repository,
        int pullRequest,
        string commentId,
        string body,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (pullRequest <= 0)
        {
            throw ParameterValidationException.For(parameterName: nameof(pullRequest), rule: "must be above 0");
        }

        string validCommentId = GitHubNames.CommentId(value: commentId, parameterName: nameof(commentId));

        if (string.IsNullOrWhiteSpace(body))
        {
            throw ParameterValidationException.For(
                parameterName: nameof(body),
                rule: "it must not be empty or whitespace"
            );
        }

        // The reply text is never echoed back: only the rule and line number are reported
        if (this._leakChecker.CheckBody(body) is { } leak)
        {
            return new(
                Outcome: PrReplyOutcome.Refused,
                Message: "the reply was refused",
                CommentId: null,
                InReplyToId: null,
                Url: null,
                Rule: $"the reply must not contain {leak.Rule}",
                LineNumber: leak.LineNumber
            );
        }

        return await this.ReplyToCommentAsync(
            validRepository: validRepository,
            pullRequest: pullRequest,
            validCommentId: validCommentId,
            body: body,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<PrReplyResult> ReplyToCommentAsync(
        string validRepository,
        int pullRequest,
        string validCommentId,
        string body,
        CancellationToken cancellationToken
    )
    {
        PullRequestReviewCommentResponse comment;

        try
        {
            comment = await this._restClient.GetAsync(
                relativeUri: $"repos/{validRepository}/pulls/comments/{validCommentId}",
                responseTypeInfo: GitHubJsonContext.Default.PullRequestReviewCommentResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return NotFound(validRepository: validRepository, pullRequest: pullRequest, validCommentId: validCommentId);
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            return Failed(outcome: PrReplyOutcome.NotPosted, message: READ_FAILED);
        }

        if (
            comment is not { Id: long id and > 0, InReplyToId: null or > 0 }
            || !StringComparer.Ordinal.Equals(x: id.ToString(CultureInfo.InvariantCulture), y: validCommentId)
        )
        {
            return Failed(outcome: PrReplyOutcome.NotPosted, message: READ_FAILED);
        }

        // A comment of another pull request in the same repository is reported as not found on this one
        if (PullRequestNumber(comment.PullRequestUrl) != pullRequest)
        {
            return NotFound(validRepository: validRepository, pullRequest: pullRequest, validCommentId: validCommentId);
        }

        // GitHub only accepts a reply to the top comment of a thread, so a reply to a reply goes to its root
        long root = comment.InReplyToId ?? id;

        return await this.PostReplyAsync(
            validRepository: validRepository,
            pullRequest: pullRequest,
            root: root,
            body: body,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<PrReplyResult> PostReplyAsync(
        string validRepository,
        int pullRequest,
        long root,
        string body,
        CancellationToken cancellationToken
    )
    {
        PullRequestReviewCommentResponse posted;

        try
        {
            posted = await this._restClient.PostAsync(
                relativeUri: $"repos/{validRepository}/pulls/{pullRequest.ToString(CultureInfo.InvariantCulture)}/comments/{root.ToString(CultureInfo.InvariantCulture)}/replies",
                body: new IssueCommentCreateRequest(Body: body),
                requestTypeInfo: GitHubJsonContext.Default.IssueCommentCreateRequest,
                responseTypeInfo: GitHubJsonContext.Default.PullRequestReviewCommentResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (GitHubHttpFailure.IsRejected(exception.StatusCode))
        {
            return Failed(
                outcome: PrReplyOutcome.NotPosted,
                message: $"the reply was not posted: {GitHubHttpFailure.Describe(exception)}"
            );
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The request was sent and never retried, so GitHub may have stored the reply without the answer arriving
            return MayHaveBeenPosted(GitHubHttpFailure.Describe(exception));
        }

        if (
            posted is not { Id: long postedId and > 0 }
            || ReplyUrl(
                htmlUrl: posted.HtmlUrl,
                validRepository: validRepository,
                pullRequest: pullRequest,
                id: postedId
            )
                is not { } url
        )
        {
            return MayHaveBeenPosted(UNEXPECTED_ANSWER);
        }

        return new(
            Outcome: PrReplyOutcome.Posted,
            Message: $"the reply was posted to the thread of comment {root.ToString(CultureInfo.InvariantCulture)}",
            CommentId: postedId,
            InReplyToId: root,
            Url: url,
            Rule: null,
            LineNumber: null
        );
    }

    private static long? PullRequestNumber(string? pullRequestUrl)
    {
        if (pullRequestUrl is null)
        {
            return null;
        }

        int index = pullRequestUrl.LastIndexOf(value: PULLS_SEGMENT, comparisonType: StringComparison.Ordinal);

        // NumberStyles.None also refuses any further path segment after the number
        return
            index >= 0
            && long.TryParse(
                s: pullRequestUrl.AsSpan(index + PULLS_SEGMENT.Length),
                style: NumberStyles.None,
                provider: CultureInfo.InvariantCulture,
                result: out long number
            )
            ? number
            : null;
    }

    private static Uri? ReplyUrl(string? htmlUrl, string validRepository, int pullRequest, long id)
    {
        if (!Uri.TryCreate(uriString: htmlUrl, uriKind: UriKind.Absolute, out Uri? uri) || !UriSchemes.IsHttp(uri))
        {
            return null;
        }

        Match match = ReplyUrlPattern().Match(htmlUrl);

        return
            match.Success
            && StringComparer.OrdinalIgnoreCase.Equals(x: match.Groups["repo"].Value, y: validRepository)
            && StringComparer.Ordinal.Equals(
                x: match.Groups["pr"].Value,
                y: pullRequest.ToString(CultureInfo.InvariantCulture)
            )
            && StringComparer.Ordinal.Equals(x: match.Groups["id"].Value, y: id.ToString(CultureInfo.InvariantCulture))
            ? uri
            : null;
    }

    private static PrReplyResult NotFound(string validRepository, int pullRequest, string validCommentId)
    {
        return Failed(
            outcome: PrReplyOutcome.NotFound,
            message: $"the comment {validCommentId} is not an inline review comment of {validRepository}#{pullRequest.ToString(CultureInfo.InvariantCulture)}"
        );
    }

    private static PrReplyResult MayHaveBeenPosted(string error)
    {
        return Failed(
            outcome: PrReplyOutcome.MayHaveBeenPosted,
            message: $"the reply may have been posted ({error}); use the pr_comments tool and look for it in the thread before posting again"
        );
    }

    private static PrReplyResult Failed(PrReplyOutcome outcome, string message)
    {
        return new(
            Outcome: outcome,
            Message: message,
            CommentId: null,
            InReplyToId: null,
            Url: null,
            Rule: null,
            LineNumber: null
        );
    }

    [GeneratedRegex(
        pattern: "^https?://[A-Za-z0-9.-]+(:[0-9]+)?/(?<repo>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)/pull/(?<pr>[0-9]+)#discussion_r(?<id>[0-9]+)$",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex ReplyUrlPattern();
}
