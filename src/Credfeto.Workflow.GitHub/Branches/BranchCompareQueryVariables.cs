using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Branches;

// Both refs are fully qualified, so a tag that shares a branch's name is never compared instead
[DebuggerDisplay("{Owner}/{Name} {BaseRef}...{HeadRef}")]
internal sealed record BranchCompareQueryVariables(
    [property: JsonPropertyName("o")] string Owner,
    [property: JsonPropertyName("r")] string Name,
    [property: JsonPropertyName("b")] string BaseRef,
    [property: JsonPropertyName("h")] string HeadRef
);
