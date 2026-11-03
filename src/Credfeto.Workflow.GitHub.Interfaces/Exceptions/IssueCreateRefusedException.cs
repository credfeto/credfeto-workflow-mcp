using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("Creating the issue was refused by a leak check")]
public sealed partial class IssueCreateRefusedException : Exception
{
    public string Subject { get; init; } = string.Empty;

    public string Rule { get; init; } = string.Empty;

    public int? LineNumber { get; init; }
}
