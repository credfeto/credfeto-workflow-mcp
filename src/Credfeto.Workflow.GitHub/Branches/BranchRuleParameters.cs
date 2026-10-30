using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Branches;

[DebuggerDisplay("{RequiredStatusChecks}")]
internal sealed record BranchRuleParameters(
    [property: JsonPropertyName("required_status_checks")] IReadOnlyList<RuleStatusCheck?>? RequiredStatusChecks
);
