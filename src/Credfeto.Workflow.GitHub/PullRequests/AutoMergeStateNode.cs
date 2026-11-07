using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("AutoMergeRequest={AutoMergeRequest}")]
internal sealed record AutoMergeStateNode(
    [property: JsonPropertyName("autoMergeRequest")] AutoMergeRequestNode? AutoMergeRequest
);
