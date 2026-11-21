using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Advisories;

[DebuggerDisplay("{Ecosystem}/{Name}")]
internal sealed record GlobalAdvisoryPackageResponse(
    [property: JsonPropertyName("ecosystem")] string? Ecosystem,
    [property: JsonPropertyName("name")] string? Name
);
