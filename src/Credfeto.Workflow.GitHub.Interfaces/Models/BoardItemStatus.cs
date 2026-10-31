using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Interfaces.Models;

// Name is only set when State is STATE
[DebuggerDisplay("State={State}, Name={Name}")]
public sealed record BoardItemStatus(BoardItemState State, string? Name);
