using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("{Project}: {Item} {Field}={Option}")]
internal sealed record UpdateItemMutationVariables(
    [property: JsonPropertyName("p")] string Project,
    [property: JsonPropertyName("i")] string Item,
    [property: JsonPropertyName("f")] string Field,
    [property: JsonPropertyName("o")] string Option
);
