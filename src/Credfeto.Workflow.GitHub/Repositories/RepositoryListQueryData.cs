using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Repositories;

[DebuggerDisplay("{RepositoryOwner}")]
internal sealed record RepositoryListQueryData(
    [property: JsonPropertyName("repositoryOwner")] RepositoryListQueryOwner? RepositoryOwner
);
