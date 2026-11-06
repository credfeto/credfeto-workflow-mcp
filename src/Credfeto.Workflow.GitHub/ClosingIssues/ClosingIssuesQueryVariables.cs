using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.ClosingIssues;

[DebuggerDisplay("{Owner}/{Name}#{Number} after={After}")]
internal sealed record ClosingIssuesQueryVariables(
    [property: JsonPropertyName("o")] string Owner,
    [property: JsonPropertyName("r")] string Name,
    [property: JsonPropertyName("n")] int Number,
    [property: JsonPropertyName("after")] string? After
);
