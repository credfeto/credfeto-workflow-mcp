using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Issue}: {Title}")]
public sealed record IssueViewResponse(
    string Issue,
    string Title,
    string State,
    IReadOnlyList<string> Labels,
    IReadOnlyList<string> Assignees,
    string Body
);
