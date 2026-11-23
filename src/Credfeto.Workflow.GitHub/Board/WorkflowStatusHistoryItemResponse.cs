using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("TimelineItems={TimelineItems}")]
internal sealed record WorkflowStatusHistoryItemResponse(
    [property: JsonPropertyName("timelineItems")] WorkflowStatusHistoryTimelineResponse? TimelineItems
);
