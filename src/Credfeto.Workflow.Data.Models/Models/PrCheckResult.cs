using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// CompletedAt and RunId are only set once the check has finished
[DebuggerDisplay("{Name}: {Verdict}")]
public sealed record PrCheckResult(string Name, PrChecksVerdict Verdict, string? CompletedAt, long? RunId);
