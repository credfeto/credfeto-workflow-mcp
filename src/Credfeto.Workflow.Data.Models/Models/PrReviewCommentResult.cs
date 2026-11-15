using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// CommentId and Url are only given when the outcome is Posted, and Rule and LineNumber only when it is Refused
[DebuggerDisplay("{Outcome}: {Message}")]
public sealed record PrReviewCommentResult(
    PrReviewCommentOutcome Outcome,
    string Message,
    long? CommentId,
    Uri? Url,
    string? Rule,
    int? LineNumber
);
