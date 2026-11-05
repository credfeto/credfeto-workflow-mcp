using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}: {Issues.Count}")]
public sealed record IssueListResponse(string Repository, IReadOnlyList<IssueSummary> Issues);
