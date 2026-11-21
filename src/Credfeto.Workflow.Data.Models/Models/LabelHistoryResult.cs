using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}#{Number}: {Changes.Count}")]
public sealed record LabelHistoryResult(string Repository, int Number, IReadOnlyList<LabelHistoryEntry> Changes);
