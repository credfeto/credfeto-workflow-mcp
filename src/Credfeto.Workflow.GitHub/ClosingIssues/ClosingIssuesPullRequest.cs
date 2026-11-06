using System.Diagnostics;
using System.Text.Json.Serialization;
using Credfeto.Workflow.GitHub.PullRequests;

namespace Credfeto.Workflow.GitHub.ClosingIssues;

[DebuggerDisplay("ClosingIssuesReferences={ClosingIssuesReferences}")]
internal sealed record ClosingIssuesPullRequest(
    [property: JsonPropertyName("closingIssuesReferences")] GraphQlConnection<ClosingIssueNode>? ClosingIssuesReferences
);
