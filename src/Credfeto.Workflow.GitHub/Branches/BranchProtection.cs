using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Branches;

[DebuggerDisplay("{RequiredStatusChecks}")]
internal sealed record BranchProtection(
    [property: JsonPropertyName("required_status_checks")] RequiredStatusChecks? RequiredStatusChecks
);
