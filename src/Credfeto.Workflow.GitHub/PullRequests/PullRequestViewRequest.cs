using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("{Reference}")]
internal sealed record PullRequestViewRequest(
    string Repository,
    int PullRequest,
    string Reference,
    IReadOnlyList<string> Fields
);
