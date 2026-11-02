using Credfeto.Workflow.GitHub.Interfaces.Exceptions;

namespace Credfeto.Workflow.GitHub.Issues;

internal static class IssueCommentsFailure
{
    public static IssueCommentsIncompleteException Incomplete(string reference)
    {
        return new($"could not read all the comments of {reference}");
    }
}
