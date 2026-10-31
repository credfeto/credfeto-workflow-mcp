using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("Items={Items}")]
internal sealed record BoardItemsProject([property: JsonPropertyName("items")] BoardItemsConnection? Items);
