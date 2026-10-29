using System;

namespace Credfeto.Workflow.GitHub.Interfaces.Validation;

public static class GitHubPath
{
    // Slash is kept so a branch such as feature/x maps onto GitHub's path form rather than a single %2F segment
    public static string BranchPath(string branch)
    {
        return Uri.EscapeDataString(branch)
            .Replace(oldValue: "%2F", newValue: "/", comparisonType: StringComparison.Ordinal);
    }
}
