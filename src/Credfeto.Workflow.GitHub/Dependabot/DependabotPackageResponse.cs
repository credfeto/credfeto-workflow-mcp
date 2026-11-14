using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Dependabot;

[DebuggerDisplay("{Ecosystem}: {Name}")]
internal sealed record DependabotPackageResponse(
    [property: JsonPropertyName("ecosystem")] string? Ecosystem,
    [property: JsonPropertyName("name")] string? Name
);
