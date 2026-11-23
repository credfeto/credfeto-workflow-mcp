using System.Diagnostics;
using System.Text.Json.Serialization;
using Credfeto.Workflow.GitHub.Issues;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("{CreatedAt} {PreviousStatus} -> {Status}")]
internal sealed record WorkflowStatusHistoryEventResponse(
    [property: JsonPropertyName("createdAt")] string? CreatedAt,
    [property: JsonPropertyName("actor")] IssueUserResponse? Actor,
    [property: JsonPropertyName("previousStatus")] string? PreviousStatus,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("project")] StatusProjectReference? Project
);
