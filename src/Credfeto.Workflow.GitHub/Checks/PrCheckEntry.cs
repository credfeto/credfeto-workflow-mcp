using System.Diagnostics;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Checks;

// One check run or commit status reduced to what judging needs; a status has no app and no conclusion
[DebuggerDisplay("{Id}: {Name} {Verdict}")]
internal sealed record PrCheckEntry(
    string Name,
    long? AppId,
    bool IsStatus,
    long Id,
    string? Conclusion,
    PrChecksVerdict Verdict,
    string? CompletedAt
);
