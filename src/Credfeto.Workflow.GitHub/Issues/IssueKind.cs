using System;

namespace Credfeto.Workflow.GitHub.Issues;

internal static class IssueKind
{
    // GitHub serves pull requests from the issues endpoint too; one carries a pull_request link or a /pull/ URL
    public static bool IsPullRequest(int issue, IssueResponse response)
    {
        return response.PullRequest is not null
            || response.HtmlUrl is { } htmlUrl
                && htmlUrl.EndsWith(value: $"/pull/{issue}", comparisonType: StringComparison.Ordinal);
    }
}
