using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}: {Advisories.Count}")]
public sealed record SecurityAdvisoriesResult(string Repository, IReadOnlyList<SecurityAdvisoryEntry> Advisories);
