using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("Payload={UpdateProjectV2ItemFieldValue}")]
internal sealed record UpdateItemMutationData(
    [property: JsonPropertyName("updateProjectV2ItemFieldValue")] UpdateItemPayload? UpdateProjectV2ItemFieldValue
);
