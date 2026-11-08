using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// SinceReady counts top-level comments and submitted reviews made after the pull request became ready; Last is only
// null when there are none of either
[DebuggerDisplay("Comments={Comments}, Reviews={Reviews}, SinceReady={SinceReady}")]
public sealed record PrStatusActivity(
    int Comments,
    int Reviews,
    int Threads,
    int Unresolved,
    int SinceReady,
    PrStatusLastActivity? Last
);
