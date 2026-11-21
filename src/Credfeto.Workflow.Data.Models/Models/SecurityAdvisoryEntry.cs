using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{GhsaId}: {Severity} {State}")]
public sealed record SecurityAdvisoryEntry(string GhsaId, string Severity, string State, string Summary);
