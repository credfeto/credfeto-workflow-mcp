using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// The requested reviewer is null when the token cannot see it
[DebuggerDisplay("RequestedReviewer={RequestedReviewer}")]
internal sealed record ReviewRequestNode(
    [property: JsonPropertyName("requestedReviewer")] RequestedReviewerNode? RequestedReviewer
);
