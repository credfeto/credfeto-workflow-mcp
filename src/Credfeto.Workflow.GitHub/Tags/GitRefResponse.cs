using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Tags;

// Every member is nullable because GitHub's answer is checked rather than trusted
[DebuggerDisplay("{Ref}")]
internal sealed record GitRefResponse(
    [property: JsonPropertyName("ref")] string? Ref,
    [property: JsonPropertyName("object")] GitObjectResponse? Object
);
