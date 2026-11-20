using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Branches;

[DebuggerDisplay("{Compare}")]
internal sealed record BranchCompareRef([property: JsonPropertyName("compare")] BranchComparison? Compare);
