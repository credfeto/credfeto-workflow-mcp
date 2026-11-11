using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Repositories;

[DebuggerDisplay("{Name}")]
internal sealed record RepositoryListBranchRef([property: JsonPropertyName("name")] string? Name);
