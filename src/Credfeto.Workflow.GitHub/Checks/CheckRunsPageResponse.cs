using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Checks;

[DebuggerDisplay("{CheckRuns}")]
internal sealed record CheckRunsPageResponse(
    [property: JsonPropertyName("check_runs")] IReadOnlyList<CheckRunResponse?>? CheckRuns
);
