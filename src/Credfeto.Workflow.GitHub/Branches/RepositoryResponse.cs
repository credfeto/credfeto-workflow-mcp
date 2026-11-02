using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Branches;

// Telling a missing branch from a missing repository relies on no member; the PR checks read uses the default branch
[DebuggerDisplay("{FullName}")]
internal sealed record RepositoryResponse(
    [property: JsonPropertyName("full_name")] string? FullName,
    [property: JsonPropertyName("default_branch")] string? DefaultBranch
);
