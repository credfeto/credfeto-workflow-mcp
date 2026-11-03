using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Issue}: {Comments.Count}")]
public sealed record IssueComments(
    string Issue,
    Uri Url,
    IReadOnlyList<IssueComment> Comments,
    IssueCommentsSummary Summary
);
