using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("Searching issues was refused by a leak check")]
public sealed partial class IssueSearchRefusedException : Exception
{
    public string Subject { get; init; } = string.Empty;

    public string Rule { get; init; } = string.Empty;
}
