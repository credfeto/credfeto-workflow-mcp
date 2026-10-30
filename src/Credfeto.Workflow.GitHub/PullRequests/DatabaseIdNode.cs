using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("{FullDatabaseId}")]
internal sealed record DatabaseIdNode([property: JsonPropertyName("fullDatabaseId")] string? FullDatabaseId);
