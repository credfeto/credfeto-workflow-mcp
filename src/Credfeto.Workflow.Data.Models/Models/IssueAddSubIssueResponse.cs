using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Parent}: {SubIssue} {Outcome}")]
public sealed record IssueAddSubIssueResponse(
    string Parent,
    string SubIssue,
    string Outcome,
    Uri ParentUrl,
    Uri SubIssueUrl
);
