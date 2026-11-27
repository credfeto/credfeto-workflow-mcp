using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Credfeto.Workflow.GitHub.Interfaces;

namespace Credfeto.Workflow.GitHub.PullRequests;

internal static class PullRequestViewQuery
{
    private const string VARIABLES = "$o:String!,$r:String!,$n:Int!";

    private const string COMMITS_VARIABLE = ",$after:String";

    private const string CHECKS_VARIABLE = ",$checksAfter:String";

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
        [PullRequestViewFields.ReviewRequests] =
            "reviewRequests(first:100){totalCount nodes{requestedReviewer{__typename ... on User{login} ... on Bot{login} ... on Mannequin{login} ... on Team{slug}}}}",
        [PullRequestViewFields.Milestone] = "milestone{title}",
        [PullRequestViewFields.CrossRepository] = "isCrossRepository",
        [PullRequestViewFields.ClosingIssues] = ClosingReferenceProjector.SELECTION,

        // Aliased, because commits is already selected with other arguments when commit authors are asked for too
        [PullRequestViewFields.Checks] =
            "headCommit:commits(last:1){nodes{commit{statusCheckRollup{contexts(first:100,after:$checksAfter){totalCount pageInfo{hasNextPage endCursor} nodes{__typename ... on CheckRun{name status conclusion isRequired(pullRequestNumber:$n)} ... on StatusContext{context state isRequired(pullRequestNumber:$n)}}}}}}}",
        [PullRequestViewFields.Commits] =
            "commitIdentities:commits(first:100,after:$after){totalCount pageInfo{hasNextPage endCursor} nodes{commit{oid authors(first:100){totalCount nodes{user{login} email name}}}}}",
        [PullRequestViewFields.ChangedFiles] = "changedFiles",
        [PullRequestViewFields.Files] = "changedFiles",
        [PullRequestViewFields.Body] = "body",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static string CommitsPage { get; } = Build([PullRequestViewFields.CommitAuthors]);

    public static string CommitIdentitiesPage { get; } = Build([PullRequestViewFields.Commits]);

    public static string ChecksPage { get; } = Build([PullRequestViewFields.Checks]);

    public static bool IsKnown(string field)
    {
        return Selections.ContainsKey(field);
    }

    public static string Build(IReadOnlyList<string> fields)
    {
        // Cursors are only declared when a selection uses them, because GraphQL rejects unused variables
        bool commits =
            fields.Contains(value: PullRequestViewFields.CommitAuthors, comparer: StringComparer.Ordinal)
            || fields.Contains(value: PullRequestViewFields.Commits, comparer: StringComparer.Ordinal);
        bool checks = fields.Contains(value: PullRequestViewFields.Checks, comparer: StringComparer.Ordinal);
        string variables =
            VARIABLES + (commits ? COMMITS_VARIABLE : string.Empty) + (checks ? CHECKS_VARIABLE : string.Empty);
        string selection = string.Join(separator: ' ', values: fields.Select(field => Selections[field]));

        return $"query({variables}){{repository(owner:$o,name:$r){{pullRequest(number:$n){{{selection}}}}}}}";
    }
}
