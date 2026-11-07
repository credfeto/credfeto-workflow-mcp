using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Search;

[DebuggerDisplay("{FullName}")]
internal sealed record RepoSearchItemResponse(
    [property: JsonPropertyName("full_name")] string? FullName,
    [property: JsonPropertyName("visibility")] string? Visibility,
    [property: JsonPropertyName("archived")] bool? Archived,
    [property: JsonPropertyName("default_branch")] string? DefaultBranch
);
