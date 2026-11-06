using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("HasPlan={HasPlan}, Blocked={Blocked}, Comments={Comments.Count}")]
public sealed record IssuePlanStatus(
    bool HasPlan,
    bool Blocked,
    string? PlanAt,
    PlanAuthor? PlanBy,
    IReadOnlyList<PlanFollowUpComment> Comments
);
