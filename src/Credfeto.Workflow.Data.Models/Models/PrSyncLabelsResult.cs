using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Added holds the labels written before the outcome, in the repository's own spelling, so a Failed or Incomplete
// result still says what changed. Skipped holds the closing issues' labels the repository does not have, and
// FailedReferences the closing issues whose labels could not be read. Rule is only given when the outcome is Refused
[DebuggerDisplay("{Outcome}: {Message}")]
public sealed record PrSyncLabelsResult(
    PrSyncLabelsOutcome Outcome,
    string Message,
    Uri Url,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> FailedReferences,
    bool Assigned,
    string? Rule
);
