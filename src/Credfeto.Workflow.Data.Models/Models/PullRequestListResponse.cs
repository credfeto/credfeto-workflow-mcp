using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}: {PullRequests.Count}")]
public sealed record PullRequestListResponse(string Repository, IReadOnlyList<PullRequestListEntry> PullRequests);
