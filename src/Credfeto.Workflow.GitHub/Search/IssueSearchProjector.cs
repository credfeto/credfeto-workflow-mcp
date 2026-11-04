using System.Collections.Generic;
using System.Linq;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.PullRequests;

namespace Credfeto.Workflow.GitHub.Search;

internal static class IssueSearchProjector
{
    private const string EMPTY = "-";

    private const string OPEN = "OPEN";

    private const string CLOSED = "CLOSED";

    public static IssueSearchEntry Project(IssueSearchItemResponse? item)
    {
        if (item is null)
        {
            throw new GitHubInvalidResponseException("GitHub returned an empty issue");
        }

        int number =
            item.Number ?? throw new GitHubInvalidResponseException("GitHub returned an issue without a number");

        return new(Number: number, State: State(item.State), Title: Title(item.Title), Labels: Labels(item.Labels));
    }

    private static string State(string? state)
    {
        return state switch
        {
            "open" => OPEN,
            "closed" => CLOSED,
            _ => throw new GitHubInvalidResponseException("GitHub returned an issue with an unknown state"),
        };
    }

    private static string Title(string? title)
    {
        string cleaned = PullRequestViewText.Clean(title);

        return string.IsNullOrWhiteSpace(cleaned) ? EMPTY : cleaned;
    }

    private static IReadOnlyList<string> Labels(IReadOnlyList<IssueLabelResponse?>? labels)
    {
        return
        [
            .. (labels ?? []).Select(label =>
                UpstreamText.ReplaceLineBreaking(
                    label?.Name ?? throw new GitHubInvalidResponseException("GitHub returned a label without a name")
                )
            ),
        ];
    }
}
