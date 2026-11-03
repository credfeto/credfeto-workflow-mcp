using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Issues;

[DebuggerDisplay("Number={Number}, State={State}")]
internal sealed record IssueTimelineSourceIssueResponse(
    [property: JsonPropertyName("number")] int? Number,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("pull_request")] IssuePullRequestLinkResponse? PullRequest,
    [property: JsonPropertyName("repository_url")] string? RepositoryUrl
);
