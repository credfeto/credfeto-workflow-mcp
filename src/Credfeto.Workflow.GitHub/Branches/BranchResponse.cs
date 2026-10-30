using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Branches;

// Every member is nullable so an incomplete response is reported by the reader's own checks rather than failing the read
[DebuggerDisplay("{Name}")]
internal sealed record BranchResponse(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("commit")] BranchCommit? Commit,
    [property: JsonPropertyName("protection")] BranchProtection? Protection
);
