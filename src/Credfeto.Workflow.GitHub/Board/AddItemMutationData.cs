using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("Payload={AddProjectV2ItemById}")]
internal sealed record AddItemMutationData(
    [property: JsonPropertyName("addProjectV2ItemById")] AddItemPayload? AddProjectV2ItemById
);
