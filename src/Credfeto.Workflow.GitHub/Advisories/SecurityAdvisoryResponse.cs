using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Advisories;

[DebuggerDisplay("{GhsaId}: {Severity} {State}")]
internal sealed record SecurityAdvisoryResponse(
    [property: JsonPropertyName("ghsa_id")] string? GhsaId,
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("summary")] string? Summary
);
