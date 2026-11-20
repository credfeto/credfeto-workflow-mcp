using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// After and ChecksAfter are left out when null because the query only declares them when that list is being paged
[DebuggerDisplay("{Owner}/{Name}#{Number} after={After}")]
internal sealed record PullRequestViewQueryVariables(
    [property: JsonPropertyName("o")] string Owner,
    [property: JsonPropertyName("r")] string Name,
    [property: JsonPropertyName("n")] int Number,
    [property: JsonPropertyName("after")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? After,
    [property: JsonPropertyName("checksAfter")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? ChecksAfter = null
);
