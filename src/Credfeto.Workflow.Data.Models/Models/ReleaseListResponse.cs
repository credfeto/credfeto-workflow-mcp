using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}: {Releases.Count}")]
public sealed record ReleaseListResponse(string Repository, IReadOnlyList<ReleaseSummary> Releases);
