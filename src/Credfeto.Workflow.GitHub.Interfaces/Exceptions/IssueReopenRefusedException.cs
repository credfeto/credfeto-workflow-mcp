using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("Reopening the issue was refused by an issue reopen rule")]
public sealed partial class IssueReopenRefusedException : Exception
{
    public string Rule { get; init; } = string.Empty;

    public int? LineNumber { get; init; }
}
