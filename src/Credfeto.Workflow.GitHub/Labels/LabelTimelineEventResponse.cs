using System.Diagnostics;
using System.Text.Json.Serialization;
using Credfeto.Workflow.GitHub.Issues;

namespace Credfeto.Workflow.GitHub.Labels;

[DebuggerDisplay("Event={Event}")]
internal sealed record LabelTimelineEventResponse(
    [property: JsonPropertyName("event")] string? Event,
    [property: JsonPropertyName("actor")] IssueUserResponse? Actor,
    [property: JsonPropertyName("label")] IssueLabelResponse? Label
);
