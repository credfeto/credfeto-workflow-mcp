using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repo} {Sha}: {Verdict}")]
public sealed record PrChecksResponse(
    string Repo,
    string Sha,
    PrChecksVerdict Verdict,
    int Passed,
    int Failed,
    int Pending,
    IReadOnlyList<PrCheckResult> Checks
);
