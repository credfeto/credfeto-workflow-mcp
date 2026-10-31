using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("ProjectItems={ProjectItems}")]
internal sealed record StatusQueryItem(
    [property: JsonPropertyName("projectItems")] StatusProjectItemsConnection? ProjectItems
);
