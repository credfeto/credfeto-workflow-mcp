using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("Id={Id}, InReplyToId={InReplyToId}")]
internal sealed record PullRequestReviewCommentResponse(
    [property: JsonPropertyName("id")] long? Id,
    [property: JsonPropertyName("in_reply_to_id")] long? InReplyToId,
    [property: JsonPropertyName("pull_request_url")] string? PullRequestUrl,
    [property: JsonPropertyName("html_url")] string? HtmlUrl
);
