using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Branches;

// Only fetched to tell a missing branch from a missing repository, so no member is relied upon
[DebuggerDisplay("{FullName}")]
internal sealed record RepositoryResponse([property: JsonPropertyName("full_name")] string? FullName);
