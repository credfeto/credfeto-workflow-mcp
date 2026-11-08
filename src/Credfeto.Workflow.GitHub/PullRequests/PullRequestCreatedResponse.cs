using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Every member is nullable so an incomplete response is reported by the service's own checks rather than failing the read
[DebuggerDisplay("#{Number}: {CreatedAt}")]
internal sealed record PullRequestCreatedResponse(
    [property: JsonPropertyName("number")] int? Number,
    [property: JsonPropertyName("created_at")] string? CreatedAt
);
