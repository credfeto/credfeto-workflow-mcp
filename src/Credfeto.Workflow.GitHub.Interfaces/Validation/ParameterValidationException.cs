using System;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Validation;

[Description("A parameter value is not valid")]
public sealed partial class ParameterValidationException : Exception
{
    public string ParameterName { get; init; } = string.Empty;

    public string Rule { get; init; } = string.Empty;

    // The rejected value is never part of the message, because callers can see it and it may be hostile or sensitive
    public static ParameterValidationException For(string parameterName, string rule)
    {
        return new($"Parameter '{parameterName}' is not valid: {rule}") { ParameterName = parameterName, Rule = rule };
    }
}
