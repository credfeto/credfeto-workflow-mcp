using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Repositories;

[DebuggerDisplay("{Visibility}")]
internal sealed record RepoViewRepository(
    [property: JsonPropertyName("defaultBranchRef")] RepositoryListBranchRef? DefaultBranchRef,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("visibility")] string? Visibility,
    [property: JsonPropertyName("mergeCommitAllowed")] bool? MergeCommitAllowed,
    [property: JsonPropertyName("squashMergeAllowed")] bool? SquashMergeAllowed,
    [property: JsonPropertyName("rebaseMergeAllowed")] bool? RebaseMergeAllowed
);
