using System;
using System.Collections.Generic;
using System.Linq;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Issues;

namespace Credfeto.Workflow.GitHub.PullRequests;

internal static class PullRequestListProjector
{
    private const string EMPTY = "-";

    private const string DRAFT = "draft";

    private const string READY = "ready";

    private const string ISSUE_FLAG = "issue";

    private const string GITHUB_ACTIONS_FLAG = "github-actions";

    private const string FORK_FLAG = "fork";

    private const string GITHUB_ACTIONS_AUTHOR = "app/github-actions";

    public static bool HasHead(PullRequestListItemResponse item, string? head)
    {
        return head is null || StringComparer.Ordinal.Equals(x: item.Head?.Ref, y: head);
    }

    public static bool HasEveryLabel(PullRequestListItemResponse item, IReadOnlyList<string> labels)
    {
        if (labels.Count == 0)
        {
            return true;
        }

        IReadOnlyList<string> present = LabelNames(item.Labels);

        return labels.All(label => present.Contains(value: label, comparer: StringComparer.OrdinalIgnoreCase));
    }

    public static PullRequestListEntry Project(PullRequestListItemResponse item, int? issue)
    {
        int number =
            item.Number ?? throw new GitHubInvalidResponseException("GitHub returned a pull request without a number");
        string author = Author(item.User);

        return new(
            Number: number,
            Head: Field(item.Head?.Ref),
            Base: Field(item.Base?.Ref),
            Author: Field(author),
            State: item.Draft == true ? DRAFT : READY,
            Flags: Flags(item: item, issue: issue, author: author),
            Url: Url(item.HtmlUrl),
            Title: Field(item.Title)
        );
    }

    private static IReadOnlyList<string> LabelNames(IReadOnlyList<PullRequestListLabelResponse?>? labels)
    {
        return
        [
            .. (labels ?? []).Select(label =>
                label?.Name ?? throw new GitHubInvalidResponseException("GitHub returned a label without a name")
            ),
        ];
    }

    private static IReadOnlyList<string> Flags(PullRequestListItemResponse item, int? issue, string author)
    {
        List<string> flags = [];

        if (issue is int number && MentionsIssue(headRef: item.Head?.Ref, issue: number))
        {
            flags.Add(ISSUE_FLAG);
        }

        if (StringComparer.Ordinal.Equals(x: author, y: GITHUB_ACTIONS_AUTHOR))
        {
            flags.Add(GITHUB_ACTIONS_FLAG);
        }

        if (IsFork(item))
        {
            flags.Add(FORK_FLAG);
        }

        return flags;
    }

    // A branch names its issue as a whole run of digits, so 12 matches feat/12-x or fix/012 but never feat/123
    private static bool MentionsIssue(string? headRef, int issue)
    {
        if (string.IsNullOrEmpty(headRef))
        {
            return false;
        }

        string wanted = issue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int index = 0;

        while (index < headRef.Length)
        {
            if (!char.IsAsciiDigit(headRef[index]))
            {
                index++;

                continue;
            }

            int start = index;

            while (index < headRef.Length && char.IsAsciiDigit(headRef[index]))
            {
                index++;
            }

            if (StringComparer.Ordinal.Equals(x: headRef[start..index].TrimStart('0'), y: wanted))
            {
                return true;
            }
        }

        return false;
    }

    // A deleted head repository leaves no repo, which can only be a fork that has gone away
    private static bool IsFork(PullRequestListItemResponse item)
    {
        string? headRepository = item.Head?.Repository?.FullName;

        return headRepository is null
            || !StringComparer.OrdinalIgnoreCase.Equals(x: headRepository, y: item.Base?.Repository?.FullName);
    }

    // A deleted account leaves no author, which is reported as an empty author rather than refused
    private static string Author(PullRequestListUserResponse? user)
    {
        if (string.IsNullOrEmpty(user?.Login))
        {
            return string.Empty;
        }

        string type = user.Type ?? string.Empty;

        return type switch
        {
            "User" or "" => user.Login,
            "Bot" => "app/" + IssueCommentsProjector.StripBotSuffix(user.Login),
            _ => type.ToLowerInvariant() + "/" + user.Login,
        };
    }

    private static Uri Url(string? htmlUrl)
    {
        if (
            htmlUrl is null
            || !Uri.TryCreate(uriString: htmlUrl, uriKind: UriKind.Absolute, out Uri? uri)
            || !UriSchemes.IsHttp(uri)
        )
        {
            throw new GitHubInvalidResponseException("GitHub returned a pull request without a valid link");
        }

        return uri;
    }

    private static string Field(string? value)
    {
        string cleaned = PullRequestViewText.Clean(value);

        return string.IsNullOrWhiteSpace(cleaned) ? EMPTY : cleaned;
    }
}
