using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// State is only given when the item has a workflow status
[DebuggerDisplay("{Repository}#{Number} {Kind}: {State}")]
public sealed record WorkflowBoardItem(WorkflowBoardItemKind Kind, string Repository, int Number, string? State);
