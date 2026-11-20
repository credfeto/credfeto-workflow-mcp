using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Branches;

// Every member is nullable because GitHub's answer is checked rather than trusted
[DebuggerDisplay("{Status} +{AheadBy} -{BehindBy}")]
internal sealed record BranchComparison(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("aheadBy")] int? AheadBy,
    [property: JsonPropertyName("behindBy")] int? BehindBy
);
