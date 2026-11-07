using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Runs;

[DebuggerDisplay("{TotalCount}")]
internal sealed record WorkflowsPageResponse(
    [property: JsonPropertyName("total_count")] int? TotalCount,
    [property: JsonPropertyName("workflows")] IReadOnlyList<WorkflowResponse?>? Workflows
);
