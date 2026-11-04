using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Rule and Message are only given when the outcome is Refused, and Line only when the rule applies to one line. The
// matched text is never part of the result, so a refusal cannot echo the leak back
[DebuggerDisplay("{Outcome}: {Rule} (line {Line})")]
public sealed record BodyCheckResult(BodyCheckOutcome Outcome, string? Rule, int? Line, string? Message);
