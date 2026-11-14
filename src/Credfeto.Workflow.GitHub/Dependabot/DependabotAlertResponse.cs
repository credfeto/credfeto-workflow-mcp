using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Dependabot;

[DebuggerDisplay("{Number}: {State}")]
internal sealed record DependabotAlertResponse(
    [property: JsonPropertyName("number")] int? Number,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("dependency")] DependabotDependencyResponse? Dependency,
    [property: JsonPropertyName("security_advisory")] DependabotAdvisoryResponse? SecurityAdvisory,
    [property: JsonPropertyName("security_vulnerability")] DependabotVulnerabilityResponse? SecurityVulnerability
);
