using System;
using System.Net.Http;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Posts one inline review comment, or a reply to one, and sorts GitHub's answer into posted, not posted or unknown
internal static class ReviewCommentPoster
{
    private const string UNEXPECTED_ANSWER = "unexpected answer";

    public static async ValueTask<ReviewCommentPostAnswer> PostAsync<TRequest>(
        IGitHubRestClient restClient,
        string relativeUri,
        TRequest body,
        JsonTypeInfo<TRequest> requestTypeInfo,
        string validRepository,
        int pullRequest,
        CancellationToken cancellationToken
    )
    {
        PullRequestReviewCommentResponse posted;

        try
        {
            posted = await restClient.PostAsync(
                relativeUri: relativeUri,
                body: body,
                requestTypeInfo: requestTypeInfo,
                responseTypeInfo: GitHubJsonContext.Default.PullRequestReviewCommentResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (GitHubHttpFailure.IsRejected(exception.StatusCode))
        {
            return NotPosted(GitHubHttpFailure.Describe(exception));
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The request was sent and never retried, so GitHub may have stored the comment without the answer arriving
            return MayHaveBeenPosted(GitHubHttpFailure.Describe(exception));
        }

        if (
            posted is not { Id: long postedId and > 0 }
            || ReviewCommentUrl.Parse(
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

        return new(CommentId: postedId, Url: url, Rejected: false, Error: string.Empty);
    }

    private static ReviewCommentPostAnswer NotPosted(string error)
    {
        return new(CommentId: null, Url: null, Rejected: true, Error: error);
    }

    private static ReviewCommentPostAnswer MayHaveBeenPosted(string error)
    {
        return new(CommentId: null, Url: null, Rejected: false, Error: error);
    }
}
