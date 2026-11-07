using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.PullRequests;

namespace Credfeto.Workflow.GitHub.Search;

internal static class PrSearchProjector
{
    private const string EMPTY = "-";

    private const string OPEN = "OPEN";

    private const string CLOSED = "CLOSED";

    private const string MERGED = "MERGED";

    public static PrSearchEntry Project(PrSearchItemResponse? item)
    {
        if (item is null)
        {
            throw new GitHubInvalidResponseException("GitHub returned an empty pull request");
        }

        int number =
            item.Number ?? throw new GitHubInvalidResponseException("GitHub returned a pull request without a number");

        PrSearchPullRequestResponse pullRequest =
            item.PullRequest
            ?? throw new GitHubInvalidResponseException("GitHub returned a pull request search item that is not one");

        // The search API does not return the head branch; reading it would cost one more request per result
        return new(
            Number: number,
            State: State(state: item.State, pullRequest: pullRequest),
            HeadBranch: EMPTY,
            Title: Title(item.Title)
        );
    }

    private static string State(string? state, PrSearchPullRequestResponse pullRequest)
    {
        return state switch
        {
            "open" => OPEN,
            "closed" when pullRequest.MergedAt is not null => MERGED,
            "closed" => CLOSED,
            _ => throw new GitHubInvalidResponseException("GitHub returned a pull request with an unknown state"),
        };
    }

    private static string Title(string? title)
    {
        string cleaned = PullRequestViewText.Clean(title);

        return string.IsNullOrWhiteSpace(cleaned) ? EMPTY : cleaned;
    }
}
