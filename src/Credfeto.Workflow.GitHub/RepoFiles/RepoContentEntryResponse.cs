using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.RepoFiles;

[DebuggerDisplay("{Type}: {Name}")]
internal sealed record RepoContentEntryResponse(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("name")] string? Name
);
