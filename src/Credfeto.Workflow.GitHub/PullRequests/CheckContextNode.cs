using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// A check run has a name, status and conclusion; a status context has a context and state instead. Both say whether
// the pull request requires them
[DebuggerDisplay("{TypeName} {Name}{Context}")]
internal sealed record CheckContextNode(
    [property: JsonPropertyName("__typename")] string? TypeName,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("conclusion")] string? Conclusion,
    [property: JsonPropertyName("context")] string? Context,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("isRequired")] bool? IsRequired
);
