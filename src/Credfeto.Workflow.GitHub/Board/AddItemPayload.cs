using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("Item={Item}")]
internal sealed record AddItemPayload([property: JsonPropertyName("item")] GraphQlNode? Item);
