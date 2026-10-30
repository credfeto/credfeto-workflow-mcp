using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Every member is nullable so an incomplete node is reported by the reader's own checks rather than failing the page
[DebuggerDisplay("{FullDatabaseId} by {Author}")]
internal sealed record ReviewCommentNode(
    [property: JsonPropertyName("fullDatabaseId")] string? FullDatabaseId,
    [property: JsonPropertyName("author")] AuthorNode? Author,
    [property: JsonPropertyName("authorAssociation")] string? AuthorAssociation,
    [property: JsonPropertyName("createdAt")] string? CreatedAt,
    [property: JsonPropertyName("isMinimized")] bool? IsMinimized,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("replyTo")] DatabaseIdNode? ReplyTo,
    [property: JsonPropertyName("pullRequestReview")] DatabaseIdNode? PullRequestReview
);
