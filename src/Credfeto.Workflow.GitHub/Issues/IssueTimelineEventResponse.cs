using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Issues;

[DebuggerDisplay("Event={Event}")]
internal sealed record IssueTimelineEventResponse(
    [property: JsonPropertyName("event")] string? Event,
    [property: JsonPropertyName("source")] IssueTimelineSourceResponse? Source
);
