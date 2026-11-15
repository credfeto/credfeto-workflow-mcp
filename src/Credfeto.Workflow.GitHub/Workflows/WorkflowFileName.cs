using System.Text.RegularExpressions;

namespace Credfeto.Workflow.GitHub.Workflows;

internal static partial class WorkflowFileName
{
    private const int MAX_LENGTH = 255;
    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    // A bare file name with no path, so it can be placed in a request path without escaping
    public static bool IsValid(string? value)
    {
        return !string.IsNullOrEmpty(value) && value.Length <= MAX_LENGTH && Pattern().IsMatch(value);
    }

    [GeneratedRegex(
        pattern: @"^[A-Za-z0-9_][A-Za-z0-9_.-]*\.ya?ml\z",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex Pattern();
}
