using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("{Project}: {Content}")]
internal sealed record AddItemMutationVariables(
    [property: JsonPropertyName("p")] string Project,
    [property: JsonPropertyName("c")] string Content
);
