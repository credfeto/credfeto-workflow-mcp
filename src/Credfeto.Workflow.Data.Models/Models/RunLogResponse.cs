using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Log is set only for the Log outcome, and is empty when the run has no failed steps
[DebuggerDisplay("{Outcome}: {Message}")]
public sealed record RunLogResponse(
    RunLogOutcome Outcome,
    string Message,
    string? Log,
    int Jobs,
    int Steps,
    int Lines,
    int Redacted,
    int Masked
);
