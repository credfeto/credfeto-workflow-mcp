using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Runs;

[DebuggerDisplay("{TotalCount}")]
internal sealed record WorkflowRunsPageResponse(
    [property: JsonPropertyName("total_count")] int? TotalCount,
    [property: JsonPropertyName("workflow_runs")] IReadOnlyList<WorkflowRunResponse?>? WorkflowRuns
);
