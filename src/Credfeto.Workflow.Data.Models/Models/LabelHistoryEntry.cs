using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Time} {Action} {Label} by {Actor}")]
public sealed record LabelHistoryEntry(string Action, string Time, string Actor, string Label);
