using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Checks;

// Every member is nullable so an incomplete response is reported by the evaluator's own checks rather than failing the read
[DebuggerDisplay("{Id}: {Context} {State}")]
internal sealed record CommitStatusResponse(
    [property: JsonPropertyName("id")] long? Id,
    [property: JsonPropertyName("context")] string? Context,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("updated_at")] string? UpdatedAt
);
