using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Issues;

namespace Credfeto.Workflow.GitHub.PullRequests;

internal static class PrStatusActivityCounter
{
    private const string PENDING_REVIEW = "PENDING";

    // Only top-level comments and submitted reviews count; a pending review has not been submitted, so it has no
    // time and nobody but its author can see it
    public static PrStatusActivity Count(PullRequestComments comments, DateTime readyAt)
    {
        IReadOnlyList<Activity> items =
        [
            .. comments
                .Comments.Select(comment => (At: comment.CreatedAt, comment.Author))
                .Concat(
                    comments
                        .Reviews.Where(review =>
                            review.SubmittedAt.Length != 0
                            && !StringComparer.Ordinal.Equals(x: review.State, y: PENDING_REVIEW)
                        )
                        .Select(review => (At: review.SubmittedAt, review.Author))
                )
                .Select(item => Parse(reference: comments.Pr, at: item.At, author: item.Author)),
        ];

        // Items are compared in list order, so of two with the same time the later one is taken
        Activity? last = items.Aggregate(
            seed: (Activity?)null,
            func: (newest, item) => newest is null || item.Time >= newest.Time ? item : newest
        );

        return new(
            Comments: comments.Summary.Comments,
            Reviews: comments.Summary.Reviews,
            Threads: comments.Summary.Threads,
            Unresolved: comments.Summary.Unresolved,
            SinceReady: items.Count(item => item.Time > readyAt),
            Last: last is null ? null : new(At: last.At, Author: last.Author)
        );
    }

    private static Activity Parse(string reference, string at, string author)
    {
        return IssueCommentsProjector.TryParseTimestamp(value: at, out DateTime time)
            ? new(At: at, Author: author, Time: time)
            : throw PullRequestCommentsFailure.Incomplete(reference);
    }

    [DebuggerDisplay("{Author}: {At}")]
    private sealed record Activity(string At, string Author, DateTime Time);
}
