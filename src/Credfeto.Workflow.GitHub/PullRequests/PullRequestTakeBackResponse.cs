using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Every member is nullable so an incomplete response is reported by the service's own checks rather than failing the
// read. GitHub always sends auto_merge, as null when auto-merge is off and as an object when it is on; it is kept as an
// element so a missing auto_merge (Undefined) is told apart from one that is off (Null) and never read as off
[DebuggerDisplay("{State}: draft {Draft}")]
internal sealed record PullRequestTakeBackResponse(
    [property: JsonPropertyName("node_id")] string? NodeId,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("draft")] bool? Draft,
    [property: JsonPropertyName("auto_merge")] JsonElement AutoMerge
);
