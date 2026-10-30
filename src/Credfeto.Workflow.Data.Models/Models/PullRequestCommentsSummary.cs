using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("Comments={Comments}, Reviews={Reviews}, Threads={Threads}, Unresolved={Unresolved}")]
public sealed record PullRequestCommentsSummary(
    int Comments,
    int Reviews,
    int ReviewsEmpty,
    int Threads,
    int Unresolved,
    int Inline
);
