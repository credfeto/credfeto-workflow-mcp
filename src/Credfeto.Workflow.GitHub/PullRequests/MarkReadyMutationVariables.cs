using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("{PullRequest}")]
internal sealed record MarkReadyMutationVariables([property: JsonPropertyName("p")] string PullRequest);
