using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Path}: {State} {Name}")]
public sealed record WorkflowEntry(string Name, string Path, string State);
