using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Run and Url are only given when the outcome is Success and the new run is already visible; Rule only when it is
// Refused
[DebuggerDisplay("{Outcome}: {Message}")]
public sealed record WorkflowRunResult(
    WorkflowRunOutcome Outcome,
    string Message,
    string Workflow,
    string? Ref,
    long? Run,
    Uri? Url,
    string? Rule
);
