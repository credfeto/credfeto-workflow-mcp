using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Time} {PreviousStatus} -> {Status} by {Actor}")]
public sealed record WorkflowStatusHistoryEntry(string Time, string Actor, string PreviousStatus, string Status);
