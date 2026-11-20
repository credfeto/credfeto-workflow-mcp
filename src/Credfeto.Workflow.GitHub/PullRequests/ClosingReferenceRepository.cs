using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("{NameWithOwner}")]
internal sealed record ClosingReferenceRepository([property: JsonPropertyName("nameWithOwner")] string? NameWithOwner);
