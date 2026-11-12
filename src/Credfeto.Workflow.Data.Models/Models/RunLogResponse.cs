using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Log is set only for the Log outcome, and is empty when the run has no failed steps. Matches is set only when the
// log was searched, and counts every matching line, kept or not; Truncated is true when more lines matched than
// were kept
[DebuggerDisplay("{Outcome}: {Message}")]
public sealed record RunLogResponse(
    RunLogOutcome Outcome,
    string Message,
    string? Log,
    int Jobs,
    int Steps,
    int Lines,
    int Redacted,
    int Masked,
    int? Matches,
    bool Truncated
);
