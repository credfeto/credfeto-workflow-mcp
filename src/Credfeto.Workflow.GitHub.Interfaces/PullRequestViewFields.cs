using System;
using System.Collections.Generic;
using System.Linq;

namespace Credfeto.Workflow.GitHub.Interfaces;

public static class PullRequestViewFields
{
    public const string State = "state";
    public const string Draft = "draft";
    public const string Title = "title";
    public const string Url = "url";
    public const string Base = "base";
    public const string Head = "head";
    public const string HeadSha = "head-sha";
    public const string AutoMerge = "auto-merge";
    public const string ReviewDecision = "review-decision";
    public const string MergeState = "merge-state";
    public const string Mergeable = "mergeable";
    public const string CreatedAt = "created-at";
    public const string MergedAt = "merged-at";
    public const string MergeCommit = "merge-commit";
    public const string Labels = "labels";
    public const string Assignees = "assignees";
    public const string Author = "author";
    public const string CommitAuthors = "commit-authors";
    public const string Files = "files";
    public const string Body = "body";

    // The canonical order; results always follow it whatever order the fields were asked for in
    public static IReadOnlyList<string> All { get; } =
    [
        State,
        Draft,
        Title,
        Url,
        Base,
        Head,
        HeadSha,
        AutoMerge,
        ReviewDecision,
        MergeState,
        Mergeable,
        CreatedAt,
        MergedAt,
        MergeCommit,
        Labels,
        Assignees,
        Author,
        CommitAuthors,
        Files,
        Body,
    ];

    // Files and body can be large, so they are only returned when asked for on their own
    // Declared after All, so All is already initialised when this is built
    public static IReadOnlyList<string> Default { get; } =
    [
        .. All.Where(field =>
            !StringComparer.Ordinal.Equals(x: field, y: Files) && !StringComparer.Ordinal.Equals(x: field, y: Body)
        ),
    ];
}
