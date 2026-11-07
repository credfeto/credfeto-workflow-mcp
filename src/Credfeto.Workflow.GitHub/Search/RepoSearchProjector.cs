using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.PullRequests;

namespace Credfeto.Workflow.GitHub.Search;

internal static class RepoSearchProjector
{
    private const string EMPTY = "-";

    private const string ACTIVE = "active";

    private const string ARCHIVED = "archived";

    public static RepoSearchEntry Project(RepoSearchItemResponse? item)
    {
        if (item is null)
        {
            throw new GitHubInvalidResponseException("GitHub returned an empty repository");
        }

        string repository = PullRequestViewText.Clean(item.FullName);

        if (string.IsNullOrWhiteSpace(repository))
        {
            throw new GitHubInvalidResponseException("GitHub returned a repository without a name");
        }

        bool archived =
            item.Archived
            ?? throw new GitHubInvalidResponseException("GitHub returned a repository without an archived flag");

        return new(
            Repository: repository,
            Visibility: Visibility(item.Visibility),
            Status: archived ? ARCHIVED : ACTIVE,
            DefaultBranch: DefaultBranch(item.DefaultBranch)
        );
    }

    private static string Visibility(string? visibility)
    {
        return visibility switch
        {
            "public" => "PUBLIC",
            "private" => "PRIVATE",
            "internal" => "INTERNAL",
            _ => throw new GitHubInvalidResponseException("GitHub returned a repository with an unknown visibility"),
        };
    }

    private static string DefaultBranch(string? defaultBranch)
    {
        string cleaned = PullRequestViewText.Clean(defaultBranch);

        return string.IsNullOrWhiteSpace(cleaned) ? EMPTY : cleaned;
    }
}
