using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// CompletedAt and RunId are only set once the check has finished. Reported is false, and Kind null, when nothing has
// reported the check on the commit, so a check that has not started can be told from one still running
[DebuggerDisplay("{Name}: {Verdict}")]
public sealed record PrCheckResult(
    string Name,
    PrChecksVerdict Verdict,
    string? CompletedAt,
    long? RunId,
    bool Required,
    bool Reported,
    PrCheckKind? Kind
);
