using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Id}: {Author}")]
public sealed record PullRequestComment(
    string Id,
    string Author,
    string Association,
    string CreatedAt,
    bool Minimized,
    Uri Url,
    string Body
);
