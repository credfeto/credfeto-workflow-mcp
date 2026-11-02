using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Issues;

[DebuggerDisplay("Title={Title}, State={State}")]
internal sealed record IssueResponse(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("labels")] IssueLabelResponse?[]? Labels,
    [property: JsonPropertyName("assignees")] IssueUserResponse?[]? Assignees,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("pull_request")] IssuePullRequestLinkResponse? PullRequest,
    [property: JsonPropertyName("html_url")] string? HtmlUrl
);
