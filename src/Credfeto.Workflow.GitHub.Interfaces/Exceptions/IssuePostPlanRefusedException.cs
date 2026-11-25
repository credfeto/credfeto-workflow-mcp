using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

// Every check runs before the first write, so a refusal always means nothing was written
[Description("Posting the plan was refused before anything was written")]
public sealed partial class IssuePostPlanRefusedException : Exception
{
    public string Rule { get; init; } = string.Empty;

    public int? LineNumber { get; init; }
}
