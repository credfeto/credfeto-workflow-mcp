using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Dependabot;

[DebuggerDisplay("{GhsaId}: {Severity}")]
internal sealed record DependabotAdvisoryResponse(
    [property: JsonPropertyName("ghsa_id")] string? GhsaId,
    [property: JsonPropertyName("severity")] string? Severity
);
