using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("Closing or reopening the pull request was refused by a pull request state rule")]
public sealed partial class PullRequestStateChangeRefusedException : Exception
{
    public string Rule { get; init; } = string.Empty;

    public int? LineNumber { get; init; }
}
