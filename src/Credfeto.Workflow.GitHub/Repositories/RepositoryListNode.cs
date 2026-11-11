using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Repositories;

[DebuggerDisplay("{NameWithOwner}: {Visibility}")]
internal sealed record RepositoryListNode(
    [property: JsonPropertyName("nameWithOwner")] string? NameWithOwner,
    [property: JsonPropertyName("visibility")] string? Visibility,
    [property: JsonPropertyName("isArchived")] bool? IsArchived,
    [property: JsonPropertyName("defaultBranchRef")] RepositoryListBranchRef? DefaultBranchRef
);
