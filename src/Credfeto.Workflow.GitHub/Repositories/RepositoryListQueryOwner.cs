using System.Diagnostics;
using System.Text.Json.Serialization;
using Credfeto.Workflow.GitHub.PullRequests;

namespace Credfeto.Workflow.GitHub.Repositories;

[DebuggerDisplay("{Repositories}")]
internal sealed record RepositoryListQueryOwner(
    [property: JsonPropertyName("repositories")] GraphQlConnection<RepositoryListNode>? Repositories
);
