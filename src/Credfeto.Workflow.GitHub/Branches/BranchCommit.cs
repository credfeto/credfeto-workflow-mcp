using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Branches;

[DebuggerDisplay("{Sha}")]
internal sealed record BranchCommit([property: JsonPropertyName("sha")] string? Sha);
