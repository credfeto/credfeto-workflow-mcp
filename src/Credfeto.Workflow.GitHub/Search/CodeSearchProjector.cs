using System.Linq;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.PullRequests;

namespace Credfeto.Workflow.GitHub.Search;

internal static class CodeSearchProjector
{
    public static CodeSearchEntry Project(CodeSearchItemResponse? item)
    {
        if (item is null)
        {
            throw new GitHubInvalidResponseException("GitHub returned an empty file");
        }

        string path = PullRequestViewText.Clean(item.Path);

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new GitHubInvalidResponseException("GitHub returned a file without a path");
        }

        CodeSearchTextMatchResponse?[] textMatches =
            item.TextMatches ?? throw new GitHubInvalidResponseException("GitHub returned a file without text matches");

        return new(Path: path, Matches: textMatches.Sum(Count));
    }

    private static int Count(CodeSearchTextMatchResponse? textMatch)
    {
        return textMatch?.Matches?.Length
            ?? throw new GitHubInvalidResponseException("GitHub returned a text match without matches");
    }
}
