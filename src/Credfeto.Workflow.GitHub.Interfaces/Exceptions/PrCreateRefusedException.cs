using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("Creating the pull request was refused by a leak check")]
public sealed partial class PrCreateRefusedException : Exception
{
    public string Subject { get; init; } = string.Empty;

    public string Rule { get; init; } = string.Empty;

    public int? LineNumber { get; init; }
}
