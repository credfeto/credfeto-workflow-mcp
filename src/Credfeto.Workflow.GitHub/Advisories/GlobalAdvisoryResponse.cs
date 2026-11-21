using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Advisories;

// Every member is nullable because GitHub's answer is checked rather than trusted
[DebuggerDisplay("{GhsaId}: {Severity}")]
internal sealed record GlobalAdvisoryResponse(
    [property: JsonPropertyName("ghsa_id")] string? GhsaId,
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("published_at")] string? PublishedAt,
    [property: JsonPropertyName("withdrawn_at")] string? WithdrawnAt,
    [property: JsonPropertyName("vulnerabilities")] IReadOnlyList<GlobalAdvisoryVulnerabilityResponse?>? Vulnerabilities
);
