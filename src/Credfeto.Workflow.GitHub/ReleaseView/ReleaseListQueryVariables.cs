using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.ReleaseView;

[DebuggerDisplay("{Owner}/{Name} After={After}")]
internal sealed record ReleaseListQueryVariables(
    [property: JsonPropertyName("o")] string Owner,
    [property: JsonPropertyName("r")] string Name,
    [property: JsonPropertyName("c")] string? After
);
