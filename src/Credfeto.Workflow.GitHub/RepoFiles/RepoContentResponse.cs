using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.RepoFiles;

[DebuggerDisplay("{Type}: {Size}")]
internal sealed record RepoContentResponse(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("size")] long? Size,
    [property: JsonPropertyName("encoding")] string? Encoding,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("entries")] RepoContentEntryResponse?[]? Entries
);
