using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}: {Total}")]
public sealed record WorkflowBoardItems(string Repository, int Total, IReadOnlyList<WorkflowBoardItem> Items);
