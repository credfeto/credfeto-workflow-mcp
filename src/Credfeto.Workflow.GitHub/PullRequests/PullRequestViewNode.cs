using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Only the requested selections are present in a response, so every member is nullable
[DebuggerDisplay("State={State}, Title={Title}")]
internal sealed record PullRequestViewNode(
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("isDraft")] bool? IsDraft,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("baseRefName")] string? BaseRefName,
    [property: JsonPropertyName("headRefName")] string? HeadRefName,
    [property: JsonPropertyName("headRefOid")] string? HeadRefOid,
    [property: JsonPropertyName("autoMergeRequest")] AutoMergeRequestNode? AutoMergeRequest,
    [property: JsonPropertyName("reviewDecision")] string? ReviewDecision,
    [property: JsonPropertyName("mergeStateStatus")] string? MergeStateStatus,
    [property: JsonPropertyName("mergeable")] string? Mergeable,
    [property: JsonPropertyName("createdAt")] string? CreatedAt,
    [property: JsonPropertyName("merged")] bool? Merged,
    [property: JsonPropertyName("mergedAt")] string? MergedAt,
    [property: JsonPropertyName("mergeCommit")] CommitOidNode? MergeCommit,
    [property: JsonPropertyName("labels")] GraphQlConnection<LabelNode>? Labels,
    [property: JsonPropertyName("assignees")] GraphQlConnection<AuthorNode>? Assignees,
    [property: JsonPropertyName("author")] AuthorNode? Author,
    [property: JsonPropertyName("commits")] GraphQlConnection<PullRequestCommitNode>? Commits,
    [property: JsonPropertyName("changedFiles")] int? ChangedFiles,
    [property: JsonPropertyName("body")] string? Body
);
