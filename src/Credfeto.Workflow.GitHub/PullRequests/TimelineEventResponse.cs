using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("Event={Event}, CreatedAt={CreatedAt}")]
internal sealed record TimelineEventResponse(
    [property: JsonPropertyName("event")] string? Event,
    [property: JsonPropertyName("created_at")] string? CreatedAt
);
