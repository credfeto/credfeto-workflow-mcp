using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{CreatedAt}: {Login}")]
public sealed record PlanFollowUpComment(
    string CreatedAt,
    string Login,
    string Association,
    bool HasApprovalKeyword,
    Uri Url
);
