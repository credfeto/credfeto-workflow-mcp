using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Type}: {Name}")]
public sealed record RepoFileEntry(string Type, string Name);
