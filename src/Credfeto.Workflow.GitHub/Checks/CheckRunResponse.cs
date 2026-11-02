using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Checks;

// Every member is nullable so an incomplete response is reported by the evaluator's own checks rather than failing the read
[DebuggerDisplay("{Id}: {Name} {Status} {Conclusion}")]
internal sealed record CheckRunResponse(
    [property: JsonPropertyName("id")] long? Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("conclusion")] string? Conclusion,
    [property: JsonPropertyName("completed_at")] string? CompletedAt,
    [property: JsonPropertyName("app")] CheckRunApp? App
);
