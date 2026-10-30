using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Id}: {Path}:{Line} Resolved={Resolved}")]
public sealed record PullRequestThread(
    string Id,
    bool Resolved,
    bool Outdated,
    string Path,
    string Line,
    string OriginalLine,
    int CommentCount,
    IReadOnlyList<PullRequestInlineComment> Comments
);
