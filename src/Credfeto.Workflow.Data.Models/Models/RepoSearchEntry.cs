using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}: {Visibility}, {Status}")]
public sealed record RepoSearchEntry(string Repository, string Visibility, string Status, string DefaultBranch);
