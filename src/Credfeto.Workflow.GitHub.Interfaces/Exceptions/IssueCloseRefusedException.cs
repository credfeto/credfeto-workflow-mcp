using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("Closing the issue was refused by an issue close rule")]
public sealed partial class IssueCloseRefusedException : Exception
{
    public string Rule { get; init; } = string.Empty;

    public int? LineNumber { get; init; }
}
