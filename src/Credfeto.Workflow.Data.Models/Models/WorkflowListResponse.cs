using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}: {Workflows.Count}")]
public sealed record WorkflowListResponse(string Repository, IReadOnlyList<WorkflowEntry> Workflows);
