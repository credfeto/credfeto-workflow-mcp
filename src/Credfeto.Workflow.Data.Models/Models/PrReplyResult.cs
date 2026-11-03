using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// CommentId, InReplyToId and Url are only given when the outcome is Posted, and Rule and LineNumber only when it is Refused
[DebuggerDisplay("{Outcome}: {Message}")]
public sealed record PrReplyResult(
    PrReplyOutcome Outcome,
    string Message,
    long? CommentId,
    long? InReplyToId,
    Uri? Url,
    string? Rule,
    int? LineNumber
);
