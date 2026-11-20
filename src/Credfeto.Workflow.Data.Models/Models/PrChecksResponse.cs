using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// NotReported counts the listed checks nothing has reported. RunningCheckRuns counts every check run on the commit,
// listed or not, that has no conclusion yet and is queued, in progress, waiting, pending or requested
[DebuggerDisplay("{Repo} {Sha}: {Verdict}")]
public sealed record PrChecksResponse(
    string Repo,
    string Sha,
    PrChecksVerdict Verdict,
    int Passed,
    int Failed,
    int Pending,
    int NotReported,
    int RunningCheckRuns,
    IReadOnlyList<PrCheckResult> Checks
);
