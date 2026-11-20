using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Branches;

[DebuggerDisplay("{Repository}")]
internal sealed record BranchCompareQueryData(
    [property: JsonPropertyName("repository")] BranchCompareRepository? Repository
);
