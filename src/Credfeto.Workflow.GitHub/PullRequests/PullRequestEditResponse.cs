using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Every member is nullable so an incomplete response is reported by the service's own checks rather than failing the read
[DebuggerDisplay("#{Number}: {Title}")]
internal sealed record PullRequestEditResponse(
    [property: JsonPropertyName("number")] int? Number,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("assignees")] PullRequestListUserResponse?[]? Assignees
);
