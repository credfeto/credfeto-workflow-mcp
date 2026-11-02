using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// State is only given when the outcome is Found, and Message only when it is NotOnBoard
[DebuggerDisplay("{Outcome}: {State}")]
public sealed record WorkflowStatusCheckResult(WorkflowStatusCheckOutcome Outcome, string? State, string? Message);
