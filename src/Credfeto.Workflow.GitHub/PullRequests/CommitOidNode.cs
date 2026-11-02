using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("Oid={Oid}")]
internal sealed record CommitOidNode([property: JsonPropertyName("oid")] string? Oid);
