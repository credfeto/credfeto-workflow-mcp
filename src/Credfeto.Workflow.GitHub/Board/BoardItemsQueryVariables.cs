using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("{Project} after {After}")]
internal sealed record BoardItemsQueryVariables(
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("c")] string? After
);
