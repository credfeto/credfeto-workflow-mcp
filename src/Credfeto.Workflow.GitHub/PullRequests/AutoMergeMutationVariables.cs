using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("{PullRequest} at {HeadSha}")]
internal sealed record AutoMergeMutationVariables(
    [property: JsonPropertyName("p")] string PullRequest,
    [property: JsonPropertyName("h")] string HeadSha
);
