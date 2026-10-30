using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("PullRequest={PullRequest}")]
internal sealed record PullRequestCommentsRepository(
    [property: JsonPropertyName("pullRequest")] PullRequestNode? PullRequest
);
