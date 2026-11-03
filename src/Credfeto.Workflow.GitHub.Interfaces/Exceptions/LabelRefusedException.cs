using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("A label change was refused by a label rule")]
public sealed partial class LabelRefusedException : Exception
{
    public string Label { get; init; } = string.Empty;

    public string Rule { get; init; } = string.Empty;
}
