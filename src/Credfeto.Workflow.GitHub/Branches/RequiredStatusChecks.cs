using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Branches;

[DebuggerDisplay("{EnforcementLevel}")]
internal sealed record RequiredStatusChecks(
    [property: JsonPropertyName("enforcement_level")] string? EnforcementLevel,
    [property: JsonPropertyName("contexts")] IReadOnlyList<string?>? Contexts,
    [property: JsonPropertyName("checks")] IReadOnlyList<StatusCheck?>? Checks
);
