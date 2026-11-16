using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Tags;

// Every member is nullable because GitHub's answer is checked rather than trusted
[DebuggerDisplay("{Type}: {Sha}")]
internal sealed record GitObjectResponse(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("sha")] string? Sha
);
