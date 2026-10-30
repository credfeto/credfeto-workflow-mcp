using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Branches;

// Parameters differ per rule type; only the required status checks shape is read, so other rules leave it empty
[DebuggerDisplay("{Type}")]
internal sealed record BranchRuleResponse(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("parameters")] BranchRuleParameters? Parameters
);
