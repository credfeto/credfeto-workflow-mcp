using Credfeto.Workflow.GitHub.Interfaces.Exceptions;

namespace Credfeto.Workflow.GitHub.PullRequests;

internal static class PullRequestViewFailure
{
    public static string IncompleteMessage(string reference)
    {
        return $"the pull request {reference} could not be read completely";
    }

    public static PullRequestViewFailedException Incomplete(string reference)
    {
        return new(IncompleteMessage(reference));
    }

    public static PullRequestViewFailedException FilesIncomplete(string reference, int changed, int listed)
    {
        return new($"the pull request {reference} changes {changed} files and listed only {listed}");
    }
}
