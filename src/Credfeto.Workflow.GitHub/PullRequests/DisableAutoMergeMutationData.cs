using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("Payload={DisablePullRequestAutoMerge}")]
internal sealed record DisableAutoMergeMutationData(
    [property: JsonPropertyName("disablePullRequestAutoMerge")] AutoMergePayload? DisablePullRequestAutoMerge
);
