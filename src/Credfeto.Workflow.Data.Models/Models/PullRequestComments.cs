using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Pr}: {ReviewDecision}")]
public sealed record PullRequestComments(
    string Pr,
    string ReviewDecision,
    IReadOnlyList<PullRequestComment> Comments,
    IReadOnlyList<PullRequestReview> Reviews,
    IReadOnlyList<PullRequestThread> Threads,
    PullRequestCommentsSummary Summary
);
