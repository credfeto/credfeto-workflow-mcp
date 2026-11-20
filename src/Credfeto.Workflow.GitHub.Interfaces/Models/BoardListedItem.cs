using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Interfaces.Models;

// A board can hold work items from several repositories, so each one names its own; Status is never NOT_ON_BOARD
[DebuggerDisplay("{Repository}#{Number} {Kind}: {Status}")]
public sealed record BoardListedItem(WorkItemKind Kind, string Repository, int Number, BoardItemStatus Status);
