using System.Collections.Generic;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Accumulates the head commit's status check rollup across pages, as check runs and status contexts. Names are
// flattened onto one line unless exact names were asked for
internal sealed class CheckRollupCollector
{
    private const string CHECK_RUN = "CheckRun";
    private const string STATUS_CONTEXT = "StatusContext";

    private readonly List<PullRequestCheck> _checks = [];
    private readonly bool _exactNames;
    private readonly ConnectionPager _pager;
    private readonly string _reference;

    public CheckRollupCollector(string reference, bool exactNames)
    {
        this._reference = reference;
        this._exactNames = exactNames;
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
                    Name: this.CheckName(name),
                    Status: PullRequestViewText.Clean(node.Status),
                    Conclusion: PullRequestViewText.Clean(node.Conclusion),
                    State: string.Empty,
                    Required: node.IsRequired == true
                ),
                { TypeName: STATUS_CONTEXT, Context: { } context } => new(
                    Kind: "status",
                    Name: this.CheckName(context),
                    Status: string.Empty,
                    Conclusion: string.Empty,
                    State: PullRequestViewText.Clean(node.State),
                    Required: node.IsRequired == true
                ),
                _ => throw PullRequestViewFailure.Incomplete(this._reference),
            }
        );
    }

    private string CheckName(string name)
    {
        return this._exactNames ? name : PullRequestViewText.Clean(name);
    }
}
