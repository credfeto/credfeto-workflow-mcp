using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Repositories;

// Archived is left out when null, so the query is not filtered on it and archived repositories are included
[DebuggerDisplay("{Owner} After={After} Archived={Archived}")]
internal sealed record RepositoryListQueryVariables(
    [property: JsonPropertyName("o")] string Owner,
    [property: JsonPropertyName("c")] string? After,
    [property: JsonPropertyName("a"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Archived
);
