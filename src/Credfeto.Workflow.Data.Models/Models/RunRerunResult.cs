using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Attempt is only given when the outcome is Success, and is the attempt the rerun starts; Rule only when it is Refused
[DebuggerDisplay("{Outcome}: {Message}")]
public sealed record RunRerunResult(RunRerunOutcome Outcome, string Message, long Run, int? Attempt, string? Rule);
