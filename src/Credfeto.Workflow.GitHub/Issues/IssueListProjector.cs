using System;
using System.Collections.Generic;
using System.Linq;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.PullRequests;

namespace Credfeto.Workflow.GitHub.Issues;

internal static class IssueListProjector
{
    private const string EMPTY = "-";

    // Both spellings of the On Hold label are in use, so either flags the issue
    private const string ON_HOLD_HYPHENATED = "On-Hold";

    public static IssueListItemResponse Required(IssueListItemResponse? item)
    {
        return item ?? throw new GitHubInvalidResponseException("GitHub returned an empty issue");
    }

    // The REST issue list includes pull requests, which carry a pull_request member
    public static bool IsIssue(IssueListItemResponse item)
    {
        return item.PullRequest is null;
    }

    public static IssueSummary Project(IssueListItemResponse item)
    {
        int number =
            item.Number ?? throw new GitHubInvalidResponseException("GitHub returned an issue without a number");

        IReadOnlyList<string> labels = Labels(item.Labels);

        return new(
            Number: number,
            Priority: Priority(labels),
            Blocked: labels.Any(IsBlocked),
            OnHold: labels.Any(IsOnHold),
            Assignees: Assignees(item.Assignees),
            Title: Title(item.Title),
            Labels: labels
        );
    }

    private static string? Priority(IReadOnlyList<string> labels)
    {
        return labels.Select(StandardLabels.FindPriority).OfType<string>().MinBy(StandardLabels.PriorityRank);
    }

    private static bool IsBlocked(string label)
    {
        return StringComparer.OrdinalIgnoreCase.Equals(x: label, y: StandardLabels.BLOCKED);
    }

    private static bool IsOnHold(string label)
    {
        return StringComparer.OrdinalIgnoreCase.Equals(x: label, y: StandardLabels.ON_HOLD)
            || StringComparer.OrdinalIgnoreCase.Equals(x: label, y: ON_HOLD_HYPHENATED);
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

    private static IReadOnlyList<string> Assignees(IReadOnlyList<IssueUserResponse?>? assignees)
    {
        return
        [
            .. (assignees ?? []).Select(assignee =>
                UpstreamText.ReplaceLineBreaking(
                    assignee?.Login
                        ?? throw new GitHubInvalidResponseException("GitHub returned an assignee without a login")
                )
            ),
        ];
    }
}
