using System;
using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.PullRequests;

// A pull request's state once every member GitHub returned has been checked
[DebuggerDisplay("{State}, Merged={Merged}")]
internal sealed record PullRequestCurrentState(string State, bool Merged, Uri Url);
