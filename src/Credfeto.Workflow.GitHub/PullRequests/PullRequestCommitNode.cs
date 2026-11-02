using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("Commit={Commit}")]
internal sealed record PullRequestCommitNode([property: JsonPropertyName("commit")] CommitAuthorsNode? Commit);
