using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Runs;

[DebuggerDisplay("{Id}: {Name} {Status} {Conclusion}")]
internal sealed record WorkflowJobResponse(
    [property: JsonPropertyName("id")] long? Id,
    [property: JsonPropertyName("run_id")] long? RunId,
    [property: JsonPropertyName("run_attempt")] int? RunAttempt,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("conclusion")] string? Conclusion,
    [property: JsonPropertyName("steps")] IReadOnlyList<WorkflowJobStepResponse?>? Steps
);
