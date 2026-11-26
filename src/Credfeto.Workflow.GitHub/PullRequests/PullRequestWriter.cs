using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.PullRequests;

// The pull request writes the hand-off and take-back make, kept apart so other tools can make them too. None is ever
// retried, and a failure is passed on as it is so the caller can tell a refused write from one that may have been made
internal static partial class PullRequestWriter
{
    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    private const string MARK_READY_MUTATION =
        "mutation($p:ID!){markPullRequestReadyForReview(input:{pullRequestId:$p}){pullRequest{isDraft}}}";

    private const string CONVERT_TO_DRAFT_MUTATION =
        "mutation($p:ID!){convertPullRequestToDraft(input:{pullRequestId:$p}){pullRequest{isDraft}}}";

    // Always a merge commit, and only for the head that was checked, so a later push is never merged unseen
    private const string ENABLE_AUTO_MERGE_MUTATION =
        "mutation($p:ID!,$h:GitObjectID!){enablePullRequestAutoMerge(input:{pullRequestId:$p,mergeMethod:MERGE,expectedHeadOid:$h}){pullRequest{autoMergeRequest{enabledAt}}}}";

    private const string DISABLE_AUTO_MERGE_MUTATION =
        "mutation($p:ID!){disablePullRequestAutoMerge(input:{pullRequestId:$p}){pullRequest{autoMergeRequest{enabledAt}}}}";

    public static async ValueTask UpdateTitleAndBodyAsync(
        IGitHubRestClient restClient,
        string repository,
        int number,
        string title,
        string body,
        CancellationToken cancellationToken
    )
    {
        PullRequestUpdateResponse response = await restClient.PatchAsync(
            relativeUri: $"repos/{repository}/pulls/{number.ToString(CultureInfo.InvariantCulture)}",
            body: new PullRequestUpdateRequest(Title: title, Body: body),
            requestTypeInfo: GitHubJsonContext.Default.PullRequestUpdateRequest,
            responseTypeInfo: GitHubJsonContext.Default.PullRequestUpdateResponse,
            cancellationToken: cancellationToken
        );

        if (response.Number != number)
        {
            throw new GitHubInvalidResponseException("GitHub returned another pull request for the update");
        }
    }

    public static async ValueTask MarkReadyAsync(
        IGitHubGraphQlClient graphQlClient,
        string pullRequestNodeId,
        CancellationToken cancellationToken
    )
    {
        MarkReadyMutationData data = await graphQlClient.ExecuteAsync(
            query: MARK_READY_MUTATION,
            variables: new MarkReadyMutationVariables(PullRequest: pullRequestNodeId),
            variablesTypeInfo: GitHubJsonContext.Default.MarkReadyMutationVariables,
            dataTypeInfo: GitHubJsonContext.Default.MarkReadyMutationData,
            cancellationToken: cancellationToken
        );

        if (data.MarkPullRequestReadyForReview?.PullRequest?.IsDraft is not false)
        {
            throw new GitHubInvalidResponseException("GitHub did not confirm the pull request is ready for review");
        }
    }

    public static async ValueTask ConvertToDraftAsync(
        IGitHubGraphQlClient graphQlClient,
        string pullRequestNodeId,
        CancellationToken cancellationToken
    )
    {
        ConvertToDraftMutationData data = await graphQlClient.ExecuteAsync(
            query: CONVERT_TO_DRAFT_MUTATION,
            variables: new MarkReadyMutationVariables(PullRequest: pullRequestNodeId),
            variablesTypeInfo: GitHubJsonContext.Default.MarkReadyMutationVariables,
            dataTypeInfo: GitHubJsonContext.Default.ConvertToDraftMutationData,
            cancellationToken: cancellationToken
        );

        if (data.ConvertPullRequestToDraft?.PullRequest?.IsDraft is not true)
        {
            throw new GitHubInvalidResponseException("GitHub did not confirm the pull request is a draft");
        }
    }

    public static async ValueTask EnableAutoMergeAsync(
        IGitHubGraphQlClient graphQlClient,
        string pullRequestNodeId,
        string headSha,
        CancellationToken cancellationToken
    )
    {
        AutoMergeMutationData data = await graphQlClient.ExecuteAsync(
            query: ENABLE_AUTO_MERGE_MUTATION,
            variables: new AutoMergeMutationVariables(PullRequest: pullRequestNodeId, HeadSha: headSha),
            variablesTypeInfo: GitHubJsonContext.Default.AutoMergeMutationVariables,
            dataTypeInfo: GitHubJsonContext.Default.AutoMergeMutationData,
            cancellationToken: cancellationToken
        );

        if (data.EnablePullRequestAutoMerge?.PullRequest?.AutoMergeRequest is null)
        {
            throw new GitHubInvalidResponseException("GitHub did not confirm auto-merge is on");
        }
    }

    public static async ValueTask DisableAutoMergeAsync(
        IGitHubGraphQlClient graphQlClient,
        string pullRequestNodeId,
        CancellationToken cancellationToken
    )
    {
        DisableAutoMergeMutationData data = await graphQlClient.ExecuteAsync(
            query: DISABLE_AUTO_MERGE_MUTATION,
            variables: new MarkReadyMutationVariables(PullRequest: pullRequestNodeId),
            variablesTypeInfo: GitHubJsonContext.Default.MarkReadyMutationVariables,
            dataTypeInfo: GitHubJsonContext.Default.DisableAutoMergeMutationData,
            cancellationToken: cancellationToken
        );

        // A missing pull request says nothing about auto-merge, so only one present with no request confirms it is off
        if (data.DisablePullRequestAutoMerge?.PullRequest is not { AutoMergeRequest: null })
        {
            throw new GitHubInvalidResponseException("GitHub did not confirm auto-merge is off");
        }
    }

    public static async ValueTask<Uri> PostCommentAsync(
        IGitHubRestClient restClient,
        string repository,
        int number,
        string body,
        CancellationToken cancellationToken
    )
    {
        IssueCommentResponse posted = await restClient.PostAsync(
            relativeUri: $"repos/{repository}/issues/{number.ToString(CultureInfo.InvariantCulture)}/comments",
            body: new IssueCommentCreateRequest(Body: body),
            requestTypeInfo: GitHubJsonContext.Default.IssueCommentCreateRequest,
            responseTypeInfo: GitHubJsonContext.Default.IssueCommentResponse,
            cancellationToken: cancellationToken
        );

        return
            CreatedItemUrl.TryParse(
                pattern: CommentUrl(),
                url: posted.HtmlUrl ?? string.Empty,
                out Uri? url,
                out int urlNumber
            )
            && urlNumber == number
            ? url
            : throw new GitHubInvalidResponseException("GitHub returned an unexpected URL for the comment");
    }

    // GitHub answers a comment on a pull request made through its issue number with the pull request URL
    [GeneratedRegex(
        pattern: @"^https://[A-Za-z0-9.-]+(:[0-9]+)?/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/(pull|issues)/(?<number>[0-9]+)#issuecomment-[0-9]+\z",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex CommentUrl();
}
