using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// HeadSha, ReviewRecordUrl and AutoMerge are given when the outcome is HandedOff, with AutoMergeReason when auto-merge is
// not on; StepsDone when it is NotHandedOff or MayHaveBeenHandedOff; and Rule and LineNumber only when it is Refused
[DebuggerDisplay("{Outcome}: {Message}")]
public sealed record PrHandOffResult(
    PrHandOffOutcome Outcome,
    string Message,
    string? HeadSha,
    Uri? ReviewRecordUrl,
    PrHandOffAutoMerge? AutoMerge,
    string? AutoMergeReason,
    IReadOnlyList<string>? StepsDone,
    string? Rule,
    int? LineNumber
);
