using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Issues;

[DebuggerDisplay("Id={Id}, CreatedAt={CreatedAt}")]
internal sealed record IssueCommentResponse(
    [property: JsonPropertyName("id")] long? Id,
    [property: JsonPropertyName("user")] IssueCommentUserResponse? User,
    [property: JsonPropertyName("author_association")] string? AuthorAssociation,
    [property: JsonPropertyName("created_at")] string? CreatedAt,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("body")] string? Body
);
