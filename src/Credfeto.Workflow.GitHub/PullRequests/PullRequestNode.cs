using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Each list is absent from a page that did not ask for it, so every member is nullable
[DebuggerDisplay("ReviewDecision={ReviewDecision}")]
internal sealed record PullRequestNode(
    [property: JsonPropertyName("reviewDecision")] string? ReviewDecision,
    [property: JsonPropertyName("comments")] GraphQlConnection<IssueCommentNode>? Comments,
    [property: JsonPropertyName("reviews")] GraphQlConnection<ReviewNode>? Reviews,
    [property: JsonPropertyName("reviewThreads")] GraphQlConnection<ReviewThreadNode>? ReviewThreads
);
