using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("Adding the sub-issue was refused by a sub-issue rule")]
public sealed partial class IssueAddSubIssueRefusedException : Exception
{
    public string Rule { get; init; } = string.Empty;
}
