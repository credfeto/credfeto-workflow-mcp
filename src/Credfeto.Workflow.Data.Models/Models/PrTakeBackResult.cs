using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// CommentUrl, AutoMergeWasOn and AlreadyDraft are given when the outcome is TakenBack; StepsDone when it is TakenBack,
// NotTakenBack or MayHaveBeenTakenBack, listing a step whose state already held as done; and Rule and LineNumber only
// when it is Refused
[DebuggerDisplay("{Outcome}: {Message}")]
public sealed record PrTakeBackResult(
    PrTakeBackOutcome Outcome,
    string Message,
    Uri? CommentUrl,
    bool? AutoMergeWasOn,
    bool? AlreadyDraft,
    IReadOnlyList<string>? StepsDone,
    string? Rule,
    int? LineNumber
);
