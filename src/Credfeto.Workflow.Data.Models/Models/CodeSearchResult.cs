using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}: {Files.Count}, Truncated={Truncated}")]
public sealed record CodeSearchResult(string Repository, IReadOnlyList<CodeSearchEntry> Files, bool Truncated);
