using System;
using System.Collections.Generic;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Follows one paged connection across requests, refusing anything that would make the list silently partial
internal sealed class ConnectionPager
{
    private const int PAGE_SIZE = 100;

    private readonly HashSet<string> _cursors = [with(StringComparer.Ordinal)];
    private readonly string _reference;
    private int _count;
    private int? _totalCount;

    public ConnectionPager(string reference)
    {
        this._reference = reference;
    }

    // Hands each node to accept and returns the cursor of the next page, or null once every node has been read
    public string? Accept<TNode>(GraphQlConnection<TNode>? connection, Action<TNode?> accept)
        where TNode : class
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

        foreach (TNode? node in nodes)
        {
            accept(node);
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
}
