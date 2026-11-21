using System.Diagnostics;
using System.Text.Json.Serialization;
using Credfeto.Workflow.GitHub.Issues;

namespace Credfeto.Workflow.GitHub.Labels;

[DebuggerDisplay("Event={Event}, CreatedAt={CreatedAt}")]
internal sealed record LabelHistoryEventResponse(
    [property: JsonPropertyName("event")] string? Event,
    [property: JsonPropertyName("created_at")] string? CreatedAt,
    [property: JsonPropertyName("actor")] IssueUserResponse? Actor,
    [property: JsonPropertyName("label")] IssueLabelResponse? Label
);
