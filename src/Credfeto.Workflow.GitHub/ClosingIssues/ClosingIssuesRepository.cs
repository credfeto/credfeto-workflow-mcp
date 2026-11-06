using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.ClosingIssues;

[DebuggerDisplay("PullRequest={PullRequest}")]
internal sealed record ClosingIssuesRepository(
    [property: JsonPropertyName("pullRequest")] ClosingIssuesPullRequest? PullRequest
);
