using System.Collections.Generic;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Accumulates the head commit's status check rollup across pages, as check runs and status contexts
internal sealed class CheckRollupCollector
{
    private const string CHECK_RUN = "CheckRun";
    private const string STATUS_CONTEXT = "StatusContext";

    private readonly List<PullRequestCheck> _checks = [];
    private readonly ConnectionPager _pager;
    private readonly string _reference;

    public CheckRollupCollector(string reference)
    {
        this._reference = reference;
        this._pager = new(reference);
    }

    public IReadOnlyList<PullRequestCheck> Result => this._checks;

    // Returns the cursor of the next page, or null once every check has been read
    public string? Accept(GraphQlConnection<HeadCommitNode>? headCommit)
    {
        if (headCommit?.Nodes is not { } commits)
        {
            throw PullRequestViewFailure.Incomplete(this._reference);
        }

        // A pull request with no commits, or a head commit with no checks or statuses, has an empty rollup
        if (commits.Count == 0)
        {
            return null;
        }

        RollupCommitNode commit = commits[0]?.Commit ?? throw PullRequestViewFailure.Incomplete(this._reference);

        if (commit.StatusCheckRollup is null)
        {
            return null;
        }

        return this._pager.Accept(connection: commit.StatusCheckRollup.Contexts, accept: this.AcceptContext);
    }

    private void AcceptContext(CheckContextNode? node)
    {
        this._checks.Add(
            node switch
            {
                { TypeName: CHECK_RUN, Name: { } name } => new(
                    Kind: "check-run",
                    Name: PullRequestViewText.Clean(name),
                    Status: PullRequestViewText.Clean(node.Status),
                    Conclusion: PullRequestViewText.Clean(node.Conclusion),
                    State: string.Empty
                ),
                { TypeName: STATUS_CONTEXT, Context: { } context } => new(
                    Kind: "status",
                    Name: PullRequestViewText.Clean(context),
                    Status: string.Empty,
                    Conclusion: string.Empty,
                    State: PullRequestViewText.Clean(node.State)
                ),
                _ => throw PullRequestViewFailure.Incomplete(this._reference),
            }
        );
    }
}
