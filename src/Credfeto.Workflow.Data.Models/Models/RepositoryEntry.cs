using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}: {Visibility}, Archived={Archived}")]
public sealed record RepositoryEntry(string Repository, string Visibility, bool Archived, string DefaultBranch);
