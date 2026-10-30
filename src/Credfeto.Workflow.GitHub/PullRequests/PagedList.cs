using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Gathers one connection page by page, refusing anything that shows the list changed or was cut short while it was read
[DebuggerDisplay("Items={Items.Count}, Pending={Pending}, After={After}")]
internal sealed class PagedList<TNode, TResult>
    where TNode : class
{
    private const int PAGE_SIZE = 100;

    private readonly List<TResult> _items;
    private readonly Func<TNode, TResult> _project;
    private readonly string _reference;
    private readonly HashSet<string> _seenCursors;
    private int? _totalCount;

    public PagedList(Func<TNode, TResult> project, string reference)
    {
        this._project = project;
        this._reference = reference;
        this._items = [];
        this._seenCursors = [with(StringComparer.Ordinal)];
        this.Pending = true;
    }

    public bool Pending { get; private set; }

    public string? After { get; private set; }

    public IReadOnlyList<TResult> Items => this._items;

    public void Accept(GraphQlConnection<TNode>? connection)
    {
        if (connection is null)
        {
            throw this.Incomplete();
        }

        int totalCount = this.AcceptTotalCount(connection.TotalCount);
        GraphQlPageInfo pageInfo = connection.PageInfo ?? throw this.Incomplete();
        bool hasNextPage = pageInfo.HasNextPage ?? throw this.Incomplete();
        IReadOnlyList<TNode?> nodes = connection.Nodes ?? throw this.Incomplete();

        foreach (TNode? node in nodes)
        {
            this._items.Add(this._project(node ?? throw this.Incomplete()));
        }

        if (hasNextPage)
        {
            this.AcceptNextPage(nodeCount: nodes.Count, cursor: pageInfo.EndCursor);

            return;
        }

        if (this._items.Count != totalCount)
        {
            throw this.Incomplete();
        }

        this.Pending = false;
        this.After = null;
    }

    private int AcceptTotalCount(int? totalCount)
    {
        if (totalCount is not >= 0)
        {
            throw this.Incomplete();
        }

        // A total that moves between pages means the list changed while it was read
        if (this._totalCount is not null && this._totalCount != totalCount)
        {
            throw this.Incomplete();
        }

        this._totalCount = totalCount;

        return totalCount.Value;
    }

    private void AcceptNextPage(int nodeCount, string? cursor)
    {
        // A page that is followed by another is always full, so a short one means the list changed while it was read
        if (nodeCount != PAGE_SIZE)
        {
            throw this.Incomplete();
        }

        // The cursor comes from the upstream, so a missing or repeated one must fail rather than loop forever
        if (!GraphQlCursor.IsValid(cursor) || !this._seenCursors.Add(cursor))
        {
            throw this.Incomplete();
        }

        this.After = cursor;
    }

    private Exception Incomplete()
    {
        return PullRequestCommentsFailure.Incomplete(this._reference);
    }
}
