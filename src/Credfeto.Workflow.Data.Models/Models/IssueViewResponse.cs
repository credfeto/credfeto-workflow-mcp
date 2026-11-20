using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Milestone is only given when the issue has one, and Comments only when they were asked for
[DebuggerDisplay("{Issue}: {Title}")]
public sealed record IssueViewResponse(
    string Issue,
    string Title,
    string State,
    IReadOnlyList<string> Labels,
    IReadOnlyList<string> Assignees,
    string Body,
    string? Milestone = null,
    IReadOnlyList<IssueViewComment>? Comments = null
);
