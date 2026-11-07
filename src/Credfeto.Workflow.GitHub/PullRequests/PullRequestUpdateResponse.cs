using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Every member is nullable so an incomplete response is reported by the writer's own checks rather than failing the read
[DebuggerDisplay("#{Number}: {Title}")]
internal sealed record PullRequestUpdateResponse(
    [property: JsonPropertyName("number")] int? Number,
    [property: JsonPropertyName("title")] string? Title
);
