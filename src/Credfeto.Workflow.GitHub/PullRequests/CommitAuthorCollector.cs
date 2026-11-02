using System;
using System.Collections.Generic;
using System.Linq;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Accumulates commit authors across pages, refusing anything that would make the list silently partial
internal sealed class CommitAuthorCollector
{
    private const int PAGE_SIZE = 100;

    private readonly HashSet<string> _cursors = [with(StringComparer.Ordinal)];
    private readonly List<string> _logins = [];
    private readonly string _reference;
    private readonly HashSet<string> _seenLogins = [with(StringComparer.Ordinal)];
    private int _count;
    private int? _totalCount;

    public CommitAuthorCollector(string reference)
    {
        this._reference = reference;
    }

    public string Result => string.Join(separator: ',', values: this._logins.Select(PullRequestViewText.Clean));

    // Returns the cursor of the next page, or null once every commit has been read
    public string? Accept(GraphQlConnection<PullRequestCommitNode>? connection)
    {
        if (
            connection is not { TotalCount: int total, PageInfo.HasNextPage: bool hasNextPage, Nodes: { } nodes }
            || total < 0
            || this._totalCount is not null && this._totalCount != total
        )
        {
            throw PullRequestViewFailure.Incomplete(this._reference);
        }

        this._totalCount = total;

        foreach (PullRequestCommitNode? node in nodes)
        {
            this.AcceptCommit(node);
        }

        this._count += nodes.Count;

        if (!hasNextPage)
        {
            return this._count == total ? null : throw PullRequestViewFailure.Incomplete(this._reference);
        }

        string? cursor = connection.PageInfo.EndCursor;

        if (nodes.Count != PAGE_SIZE || !GraphQlCursor.IsValid(cursor) || !this._cursors.Add(cursor))
        {
            throw PullRequestViewFailure.Incomplete(this._reference);
        }

        return cursor;
    }

    private void AcceptCommit(PullRequestCommitNode? node)
    {
        if (node?.Commit?.Authors is not { TotalCount: int total, Nodes: { } authors } || total > authors.Count)
        {
            throw PullRequestViewFailure.Incomplete(this._reference);
        }

        // An author with no linked GitHub account has no login and is left out rather than shown as blank
        this._logins.AddRange(
            authors.Select(author => author?.User?.Login).OfType<string>().Where(this._seenLogins.Add)
        );
    }
}
