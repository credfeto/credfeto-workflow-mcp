using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("{Owner}/{Name}#{Number} after {After}")]
internal sealed record WorkflowStatusHistoryQueryVariables(
    [property: JsonPropertyName("o")] string Owner,
    [property: JsonPropertyName("r")] string Name,
    [property: JsonPropertyName("n")] int Number,
    [property: JsonPropertyName("c")] string? After
);
