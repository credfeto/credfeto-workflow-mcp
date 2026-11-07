using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("Protected={Protected}")]
internal sealed record BranchProtectedResponse([property: JsonPropertyName("protected")] bool? Protected);
