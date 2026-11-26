using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// CommentUrl is given once the comment is posted, so a LabelNotAdded result still names it; Rule is only given when
// the outcome is Refused, with LineNumber for a rule that applies to one line rather than to the body as a whole
[DebuggerDisplay("{Outcome}: {Message}")]
public sealed record BlockResult(
    BlockOutcome Outcome,
    string Message,
    Uri? CommentUrl,
    bool AlreadyBlocked,
    string? Rule,
    int? LineNumber
);
