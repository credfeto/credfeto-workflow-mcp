using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Checks;

[DebuggerDisplay("{Statuses}")]
internal sealed record CombinedStatusResponse(
    [property: JsonPropertyName("statuses")] IReadOnlyList<CommitStatusResponse?>? Statuses
);
