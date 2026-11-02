using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("{Reference}")]
internal sealed record PullRequestViewRequest(string Repository, int PullRequest, IReadOnlyList<string> Fields)
{
    public string Reference => $"{this.Repository}#{this.PullRequest}";
}
