using System.Diagnostics;
using System.Text.Json.Serialization;
using Credfeto.Workflow.GitHub.Checks;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Every member is nullable so an incomplete response is reported by the service's own checks rather than failing the read
[DebuggerDisplay("{State}: {Head} into {Base}")]
internal sealed record PullRequestHandOffResponse(
    [property: JsonPropertyName("node_id")] string? NodeId,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("draft")] bool? Draft,
    [property: JsonPropertyName("labels")] PullRequestListLabelResponse?[]? Labels,
    [property: JsonPropertyName("head")] PullRequestRef? Head,
    [property: JsonPropertyName("base")] PullRequestRef? Base
);
