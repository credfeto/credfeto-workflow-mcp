using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("Payload={EnablePullRequestAutoMerge}")]
internal sealed record AutoMergeMutationData(
    [property: JsonPropertyName("enablePullRequestAutoMerge")] AutoMergePayload? EnablePullRequestAutoMerge
);
