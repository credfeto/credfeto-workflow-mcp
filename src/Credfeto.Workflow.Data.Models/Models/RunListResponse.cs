using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}: {Runs.Count} runs")]
public sealed record RunListResponse(string Repository, IReadOnlyList<RunListEntry> Runs);
