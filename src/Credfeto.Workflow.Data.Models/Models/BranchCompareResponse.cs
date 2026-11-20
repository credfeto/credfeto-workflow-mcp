using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// AheadBy counts the head's commits the base lacks, BehindBy the base's commits the head lacks
[DebuggerDisplay("{Repo} {Base}...{Head}: {Status}")]
public sealed record BranchCompareResponse(
    string Repo,
    string Base,
    string Head,
    BranchCompareStatus Status,
    int AheadBy,
    int BehindBy
);
