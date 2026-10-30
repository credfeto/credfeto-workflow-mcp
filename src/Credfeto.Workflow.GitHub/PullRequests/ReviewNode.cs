using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Every member is nullable so an incomplete node is reported by the reader's own checks rather than failing the page
[DebuggerDisplay("{FullDatabaseId} {State} by {Author}")]
internal sealed record ReviewNode(
    [property: JsonPropertyName("fullDatabaseId")] string? FullDatabaseId,
    [property: JsonPropertyName("author")] AuthorNode? Author,
    [property: JsonPropertyName("authorAssociation")] string? AuthorAssociation,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("submittedAt")] string? SubmittedAt,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("body")] string? Body
);
