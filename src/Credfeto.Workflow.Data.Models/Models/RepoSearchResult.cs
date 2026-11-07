using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Owner}: {Repositories.Count}, Truncated={Truncated}")]
public sealed record RepoSearchResult(string Owner, IReadOnlyList<RepoSearchEntry> Repositories, bool Truncated);
