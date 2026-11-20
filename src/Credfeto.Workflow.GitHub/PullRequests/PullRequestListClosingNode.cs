using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("ClosingIssuesReferences={ClosingIssuesReferences}")]
internal sealed record PullRequestListClosingNode(
    [property: JsonPropertyName("closingIssuesReferences")]
        GraphQlConnection<ClosingReferenceNode>? ClosingIssuesReferences
);
