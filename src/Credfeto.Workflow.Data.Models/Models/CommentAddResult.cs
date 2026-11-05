using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Url is only given when the outcome is Posted, and Rule only when it is Refused; LineNumber is given with a Rule
// that applies to one line rather than to the body as a whole
[DebuggerDisplay("{Outcome}: {Message}")]
public sealed record CommentAddResult(
    CommentAddOutcome Outcome,
    string Message,
    Uri? Url,
    string? Rule,
    int? LineNumber
);
