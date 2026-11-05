using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Issues;

[DebuggerDisplay("Number={Number}, Title={Title}")]
internal sealed record IssueListItemResponse(
    [property: JsonPropertyName("number")] int? Number,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("labels")] IssueLabelResponse?[]? Labels,
    [property: JsonPropertyName("assignees")] IssueUserResponse?[]? Assignees,
    [property: JsonPropertyName("pull_request")] IssuePullRequestLinkResponse? PullRequest
);
