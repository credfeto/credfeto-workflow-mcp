using System;
using System.Collections.Frozen;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.PullRequests;

namespace Credfeto.Workflow.GitHub.Repositories;

internal static class RepositoryListProjector
{
    private const string EMPTY = "-";

    private static readonly FrozenSet<string> Visibilities = FrozenSet.Create(
        equalityComparer: StringComparer.Ordinal,
        "PUBLIC",
        "PRIVATE",
        "INTERNAL"
    );

    public static RepositoryEntry Project(RepositoryListNode? node)
    {
        if (node is null)
        {
            throw new GitHubInvalidResponseException("GitHub returned an empty repository");
        }

        string repository = PullRequestViewText.Clean(node.NameWithOwner);

        if (string.IsNullOrWhiteSpace(repository))
        {
            throw new GitHubInvalidResponseException("GitHub returned a repository without a name");
        }

        bool archived =
            node.IsArchived
            ?? throw new GitHubInvalidResponseException("GitHub returned a repository without an archived flag");

        return new(
            Repository: repository,
            Visibility: Visibility(node.Visibility),
            Archived: archived,
            DefaultBranch: DefaultBranch(node.DefaultBranchRef?.Name)
        );
    }

    private static string Visibility(string? visibility)
    {
        return visibility is not null && Visibilities.Contains(visibility)
            ? visibility
            : throw new GitHubInvalidResponseException("GitHub returned a repository with an unknown visibility");
    }

    // An empty repository has no default branch
    private static string DefaultBranch(string? defaultBranch)
    {
        string cleaned = PullRequestViewText.Clean(defaultBranch);

        return string.IsNullOrWhiteSpace(cleaned) ? EMPTY : cleaned;
    }
}
