using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Owner}: {Repositories.Count}")]
public sealed record RepositoryListResponse(string Owner, IReadOnlyList<RepositoryEntry> Repositories);
