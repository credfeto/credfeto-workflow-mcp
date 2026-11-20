using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.PullRequests;

// The query that reads a further page of one list, and whether its cursor is the checks one rather than commits
[DebuggerDisplay("Checks={Checks}")]
internal readonly record struct PullRequestViewPage(string Query, bool Checks);
