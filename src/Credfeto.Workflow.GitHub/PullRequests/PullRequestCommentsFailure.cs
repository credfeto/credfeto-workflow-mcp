using Credfeto.Workflow.GitHub.Interfaces.Exceptions;

namespace Credfeto.Workflow.GitHub.PullRequests;

internal static class PullRequestCommentsFailure
{
    public static PullRequestCommentsIncompleteException Incomplete(string reference)
    {
        return new(IncompleteMessage(reference));
    }

    public static string IncompleteMessage(string reference)
    {
        return $"could not read all the comments of {reference}";
    }
}
