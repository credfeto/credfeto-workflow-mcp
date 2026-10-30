using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Id}: {Author} in reply to {InReplyTo}")]
public sealed record PullRequestInlineComment(
    string Id,
    string InReplyTo,
    string ReviewId,
    string Author,
    string Association,
    string CreatedAt,
    bool Minimized,
    Uri Url,
    string Body
);
