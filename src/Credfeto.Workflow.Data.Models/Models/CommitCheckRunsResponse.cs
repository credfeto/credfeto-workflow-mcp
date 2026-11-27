using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// CheckRuns is every check run on the commit as GitHub's default (latest) filter lists them. Running counts those with
// no conclusion whose status is queued, in progress, waiting, pending or requested
[DebuggerDisplay("{Repo} {Sha}: {Running} running")]
public sealed record CommitCheckRunsResponse(
    string Repo,
    string Sha,
    int Running,
    IReadOnlyList<CommitCheckRun> CheckRuns
);
