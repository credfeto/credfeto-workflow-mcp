using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("PullRequest={PullRequest}")]
internal sealed record MarkReadyPayload([property: JsonPropertyName("pullRequest")] DraftStateNode? PullRequest);
