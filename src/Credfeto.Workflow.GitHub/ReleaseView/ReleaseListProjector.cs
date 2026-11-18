using System.Collections.Generic;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.PullRequests;

namespace Credfeto.Workflow.GitHub.ReleaseView;

internal static class ReleaseListProjector
{
    private const string EMPTY = "-";

    // The releases connection cannot filter out drafts, and a caller who can push sees them, so they are dropped here
    public static IEnumerable<ReleaseSummary> Published(IEnumerable<ReleaseListNode?> nodes)
    {
        foreach (ReleaseListNode? node in nodes)
        {
            if (node is null)
            {
                throw new GitHubInvalidResponseException("GitHub returned an empty release");
            }

            bool isDraft = node.IsDraft
                ?? throw new GitHubInvalidResponseException("GitHub returned a release without a draft flag");

            if (!isDraft)
            {
                yield return Project(node);
            }
        }
    }

    private static ReleaseSummary Project(ReleaseListNode node)
    {
        return new(
            Tag: Required(value: node.TagName, message: "GitHub returned a release without a tag"),
            PublishedAt: Required(
                value: node.PublishedAt,
                message: "GitHub returned a published release without a date"
            ),
            IsLatest: node.IsLatest
                ?? throw new GitHubInvalidResponseException("GitHub returned a release without a latest flag"),
            IsPrerelease: node.IsPrerelease
                ?? throw new GitHubInvalidResponseException("GitHub returned a release without a pre-release flag"),
            Name: Name(node.Name)
        );
    }

    private static string Required(string? value, string message)
    {
        string cleaned = PullRequestViewText.Clean(value);

        return string.IsNullOrWhiteSpace(cleaned) ? throw new GitHubInvalidResponseException(message) : cleaned;
    }

    // A release without a title has no name of its own; "-" keeps the field non-empty, as cfwf shows it
    private static string Name(string? name)
    {
        string cleaned = PullRequestViewText.Clean(name);

        return string.IsNullOrWhiteSpace(cleaned) ? EMPTY : cleaned;
    }
}
