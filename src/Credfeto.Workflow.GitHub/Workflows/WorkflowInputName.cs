using System.Text.RegularExpressions;

namespace Credfeto.Workflow.GitHub.Workflows;

internal static partial class WorkflowInputName
{
    private const int MAX_LENGTH = 100;
    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    // The form GitHub requires of an input id, so a name that passes can be quoted in a message
    public static bool IsValid(string value)
    {
        return value.Length is > 0 and <= MAX_LENGTH && Pattern().IsMatch(value);
    }

    [GeneratedRegex(
        pattern: @"^[A-Za-z_][A-Za-z0-9_-]*\z",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex Pattern();
}
