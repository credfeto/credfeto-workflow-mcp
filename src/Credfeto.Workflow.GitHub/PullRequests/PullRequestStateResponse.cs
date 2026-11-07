using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Every member is nullable so an incomplete response is reported by the service's own checks rather than failing the read
[DebuggerDisplay("#{Number}: {State}, Merged={Merged}")]
internal sealed record PullRequestStateResponse(
    [property: JsonPropertyName("number")] int? Number,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("merged")] bool? Merged,
    [property: JsonPropertyName("html_url")] string? HtmlUrl
);
