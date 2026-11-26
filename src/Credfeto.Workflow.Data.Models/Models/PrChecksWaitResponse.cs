using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// LastRead is the last judgement made, which can still read Passed when the limit came before its confirming read
[DebuggerDisplay("{Outcome} after {Reads} reads over {WaitedSeconds} seconds")]
public sealed record PrChecksWaitResponse(
    PrChecksWaitOutcome Outcome,
    int Reads,
    int WaitedSeconds,
    PrChecksResponse LastRead
);
