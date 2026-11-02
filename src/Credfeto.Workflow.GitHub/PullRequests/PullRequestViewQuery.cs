using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Credfeto.Workflow.GitHub.Interfaces;

namespace Credfeto.Workflow.GitHub.PullRequests;

internal static class PullRequestViewQuery
{
    private const string VARIABLES = "$o:String!,$r:String!,$n:Int!";

    private const string COMMITS_VARIABLES = VARIABLES + ",$after:String";

    // The query is only ever assembled from these constant selections, never from caller text, so nothing a caller
    // passes can change its shape. Duplicate selections (merged) are merged by GraphQL
    private static readonly FrozenDictionary<string, string> Selections = new Dictionary<string, string>(
        StringComparer.Ordinal
    )
    {
        [PullRequestViewFields.State] = "state",
        [PullRequestViewFields.Draft] = "isDraft",
        [PullRequestViewFields.Title] = "title",
        [PullRequestViewFields.Url] = "url",
        [PullRequestViewFields.Base] = "baseRefName",
        [PullRequestViewFields.Head] = "headRefName",
        [PullRequestViewFields.HeadSha] = "headRefOid",
        [PullRequestViewFields.AutoMerge] = "autoMergeRequest{enabledAt}",
        [PullRequestViewFields.ReviewDecision] = "reviewDecision",
        [PullRequestViewFields.MergeState] = "mergeStateStatus",
        [PullRequestViewFields.Mergeable] = "mergeable",
        [PullRequestViewFields.CreatedAt] = "createdAt",
        [PullRequestViewFields.MergedAt] = "merged mergedAt",
        [PullRequestViewFields.MergeCommit] = "merged mergeCommit{oid}",
        [PullRequestViewFields.Labels] = "labels(first:100){totalCount nodes{name}}",
        [PullRequestViewFields.Assignees] = "assignees(first:100){totalCount nodes{login}}",
        [PullRequestViewFields.Author] = "author{login}",
        [PullRequestViewFields.CommitAuthors] =
            "commits(first:100,after:$after){totalCount pageInfo{hasNextPage endCursor} nodes{commit{authors(first:100){totalCount nodes{user{login}}}}}}",
        [PullRequestViewFields.Files] = "changedFiles",
        [PullRequestViewFields.Body] = "body",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static string CommitsPage { get; } = Build([PullRequestViewFields.CommitAuthors]);

    public static bool IsKnown(string field)
    {
        return Selections.ContainsKey(field);
    }

    public static string Build(IReadOnlyList<string> fields)
    {
        // $after is only declared when the commits selection uses it, because GraphQL rejects unused variables
        string variables = fields.Contains(value: PullRequestViewFields.CommitAuthors, comparer: StringComparer.Ordinal)
            ? COMMITS_VARIABLES
            : VARIABLES;
        string selection = string.Join(separator: ' ', values: fields.Select(field => Selections[field]));

        return $"query({variables}){{repository(owner:$o,name:$r){{pullRequest(number:$n){{{selection}}}}}}}";
    }
}
