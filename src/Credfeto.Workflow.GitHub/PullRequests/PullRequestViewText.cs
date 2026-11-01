using Credfeto.Workflow.GitHub.Http;

namespace Credfeto.Workflow.GitHub.PullRequests;

internal static class PullRequestViewText
{
    // Values are printed one per line by callers, so anything that could forge or split a line is flattened
    public static string Clean(string? value)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : UpstreamText.ReplaceLineBreaking(value);
    }
}
