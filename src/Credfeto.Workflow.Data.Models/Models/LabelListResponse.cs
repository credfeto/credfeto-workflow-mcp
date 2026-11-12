using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}: {Labels.Count}")]
public sealed record LabelListResponse(string Repository, IReadOnlyList<string> Labels);
