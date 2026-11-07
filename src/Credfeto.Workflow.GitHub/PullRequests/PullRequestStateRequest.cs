using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("State={State}")]
internal sealed record PullRequestStateRequest([property: JsonPropertyName("state")] string State);
