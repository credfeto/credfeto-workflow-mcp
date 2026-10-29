using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("{Owner}/{Name} after {After}")]
internal sealed record ProjectsQueryVariables(
    [property: JsonPropertyName("owner")] string Owner,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("after")] string? After
);
