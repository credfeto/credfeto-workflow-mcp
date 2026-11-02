using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Checks;

[DebuggerDisplay("{Ref} at {Sha}")]
internal sealed record PullRequestRef(
    [property: JsonPropertyName("ref")] string? Ref,
    [property: JsonPropertyName("sha")] string? Sha
);
