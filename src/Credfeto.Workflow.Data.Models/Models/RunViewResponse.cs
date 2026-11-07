using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository} {Id}: {Status} {Conclusion}")]
public sealed record RunViewResponse(
    string Repository,
    long Id,
    string Status,
    string Conclusion,
    string HeadSha,
    string HeadBranch,
    string Workflow,
    string Event,
    int Attempt,
    string CreatedAt,
    IReadOnlyList<RunJobEntry> Jobs
);
