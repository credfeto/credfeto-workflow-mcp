using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Runs;

[DebuggerDisplay("{Number}: {Name} {Conclusion}")]
internal sealed record WorkflowJobStepResponse(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("number")] int? Number,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("conclusion")] string? Conclusion
);
