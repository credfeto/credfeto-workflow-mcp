using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("Payload={MarkPullRequestReadyForReview}")]
internal sealed record MarkReadyMutationData(
    [property: JsonPropertyName("markPullRequestReadyForReview")] MarkReadyPayload? MarkPullRequestReadyForReview
);
