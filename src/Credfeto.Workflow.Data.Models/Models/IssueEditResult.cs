using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Url is only given when the outcome is Edited or Unchanged, and Rule and LineNumber only when it is Refused
[DebuggerDisplay("{Outcome}: {Message}")]
public sealed record IssueEditResult(IssueEditOutcome Outcome, string Message, Uri? Url, string? Rule, int? LineNumber);
