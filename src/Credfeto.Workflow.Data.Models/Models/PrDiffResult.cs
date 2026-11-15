using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Mode is given whenever it was valid, Diff only on a Success in the Diff mode, and Files only on a Success in the other modes
[DebuggerDisplay("{Outcome}: {Message}")]
public sealed record PrDiffResult(
    PrDiffOutcome Outcome,
    string Message,
    PrDiffMode? Mode,
    string? Diff,
    IReadOnlyList<PrDiffFile>? Files
);
