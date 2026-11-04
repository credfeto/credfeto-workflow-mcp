using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}: {Issues.Count}, Truncated={Truncated}")]
public sealed record IssueSearchResult(string Repository, IReadOnlyList<IssueSearchEntry> Issues, bool Truncated);
