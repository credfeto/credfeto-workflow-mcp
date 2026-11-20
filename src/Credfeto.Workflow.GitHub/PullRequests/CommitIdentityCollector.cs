using System.Collections.Generic;
using System.Linq;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Accumulates every commit with its authors' logins and emails across pages
internal sealed class CommitIdentityCollector
{
    private readonly List<PullRequestCommitIdentity> _commits = [];
    private readonly ConnectionPager _pager;
    private readonly string _reference;

    public CommitIdentityCollector(string reference)
    {
        this._reference = reference;
        this._pager = new(reference);
    }

    public IReadOnlyList<PullRequestCommitIdentity> Result => this._commits;

    // Returns the cursor of the next page, or null once every commit has been read
    public string? Accept(GraphQlConnection<CommitIdentityNode>? connection)
    {
        return this._pager.Accept(connection: connection, accept: this.AcceptCommit);
    }

    private void AcceptCommit(CommitIdentityNode? node)
    {
        if (
            node?.Commit is not { Oid: { } oid, Authors: { TotalCount: int total, Nodes: { } authors } }
            || string.IsNullOrEmpty(oid)
            || total > authors.Count
        )
        {
            throw PullRequestViewFailure.Incomplete(this._reference);
        }

        this._commits.Add(
            new(
                Oid: PullRequestViewText.Clean(oid),
                Authors:
                [
                    .. authors.Select(author =>
                        author is null
                            ? throw PullRequestViewFailure.Incomplete(this._reference)
                            : new CommitAuthorIdentity(
                                Login: PullRequestViewText.Clean(author.User?.Login),
                                Email: PullRequestViewText.Clean(author.Email),
                                Name: PullRequestViewText.Clean(author.Name)
                            )
                    ),
                ]
            )
        );
    }
}
