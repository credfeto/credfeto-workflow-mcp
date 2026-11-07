using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.PullRequests;

// The pull request writes the hand-off makes, kept apart so other tools can make them too. None is ever retried, and a
// failure is passed on as it is so the caller can tell a refused write from one that may have been made
internal static class PullRequestWriter
{
    private const string MARK_READY_MUTATION =
        "mutation($p:ID!){markPullRequestReadyForReview(input:{pullRequestId:$p}){pullRequest{isDraft}}}";

    // Always a merge commit, and only for the head that was checked, so a later push is never merged unseen
    private const string ENABLE_AUTO_MERGE_MUTATION =
        "mutation($p:ID!,$h:GitObjectID!){enablePullRequestAutoMerge(input:{pullRequestId:$p,mergeMethod:MERGE,expectedHeadOid:$h}){pullRequest{autoMergeRequest{enabledAt}}}}";

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
}
