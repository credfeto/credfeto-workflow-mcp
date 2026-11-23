using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}#{Number}: {Changes.Count}")]
public sealed record WorkflowStatusHistoryResult(
    string Repository,
    int Number,
    IReadOnlyList<WorkflowStatusHistoryEntry> Changes
);
