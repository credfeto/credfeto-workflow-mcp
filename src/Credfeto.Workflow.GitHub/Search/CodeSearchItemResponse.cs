using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Search;

[DebuggerDisplay("Path={Path}")]
internal sealed record CodeSearchItemResponse(
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("text_matches")] CodeSearchTextMatchResponse?[]? TextMatches
);
