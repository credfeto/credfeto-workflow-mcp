using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Id}: {Author}")]
public sealed record IssueComment(
    string Id,
    string Author,
    string Association,
    string CreatedAt,
    Uri Url,
    bool Truncated,
    IReadOnlyList<string> Body
);
