using System;
using System.Globalization;
using Credfeto.Workflow.GitHub.Interfaces.Validation;

namespace Credfeto.Workflow.GitHub.Dependabot;

public static class DependabotAlertsQuery
{
    public const int DEFAULT_LIMIT = 30;
    public const int MAX_LIMIT = 100;

    private const string OPEN = "open";
    private const string ALL = "all";

    private const string LIMIT_RULE = "it must be a whole number from 1 to 100";

    public static string State(string? value, string parameterName)
    {
        return value switch
        {
            null or "" => OPEN,
            OPEN or "fixed" or "dismissed" or "auto_dismissed" or ALL => value,
            _ => throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it must be open, fixed, dismissed, auto_dismissed or all"
            ),
        };
    }

    public static string? Severity(string? value, string parameterName)
    {
        return value switch
        {
            null or "" => null,
            "low" or "medium" or "high" or "critical" => value,
            _ => throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it must be low, medium, high or critical"
            ),
        };
    }

    public static int Limit(int value, string parameterName)
    {
        return value is >= 1 and <= MAX_LIMIT
            ? value
            : throw ParameterValidationException.For(parameterName: parameterName, rule: LIMIT_RULE);
    }

    // Every value here has already been validated against a fixed set, so none of them needs escaping. GitHub has no
    // state filter meaning every state, so all is asked for by leaving the filter out
    public static string RequestPath(string validRepository, string validState, string? validSeverity, int validLimit)
    {
        string state = StringComparer.Ordinal.Equals(x: validState, y: ALL) ? string.Empty : $"state={validState}&";
        string severity = validSeverity is null ? string.Empty : $"severity={validSeverity}&";

        return string.Create(
            provider: CultureInfo.InvariantCulture,
            $"repos/{validRepository}/dependabot/alerts?{state}{severity}per_page={validLimit}"
        );
    }
}
