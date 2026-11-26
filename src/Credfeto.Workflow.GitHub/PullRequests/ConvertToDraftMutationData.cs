using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("Payload={ConvertPullRequestToDraft}")]
internal sealed record ConvertToDraftMutationData(
    [property: JsonPropertyName("convertPullRequestToDraft")] MarkReadyPayload? ConvertPullRequestToDraft
);
