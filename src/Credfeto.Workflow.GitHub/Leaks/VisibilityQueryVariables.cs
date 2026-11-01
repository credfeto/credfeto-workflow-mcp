using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Leaks;

[DebuggerDisplay("{Owner}/{Name}")]
internal sealed record VisibilityQueryVariables(
    [property: JsonPropertyName("o")] string Owner,
    [property: JsonPropertyName("r")] string Name
);
