using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Verdict}")]
public sealed record PrStatusChecks(
    PrChecksVerdict Verdict,
    IReadOnlyList<string> Failing,
    IReadOnlyList<string> Pending
);
