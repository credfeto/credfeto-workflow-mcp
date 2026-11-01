using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("ProjectV2Item={ProjectV2Item}")]
internal sealed record UpdateItemPayload([property: JsonPropertyName("projectV2Item")] GraphQlNode? ProjectV2Item);
