using System.Linq;

namespace Credfeto.Workflow.GitHub.PullRequests;

internal static class PullRequestViewText
{
    // Values are printed one per line by callers, so a control character could forge or split a line
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Any(char.IsControl) ? new([.. value.Select(c => char.IsControl(c) ? ' ' : c)]) : value;
    }
}
