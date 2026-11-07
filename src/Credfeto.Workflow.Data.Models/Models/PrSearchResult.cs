using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}: {PullRequests.Count}, Truncated={Truncated}")]
public sealed record PrSearchResult(string Repository, IReadOnlyList<PrSearchEntry> PullRequests, bool Truncated);
