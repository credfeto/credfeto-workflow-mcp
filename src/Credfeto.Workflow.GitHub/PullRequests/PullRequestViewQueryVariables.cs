using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// After is left out when null because the query only declares it when commits are being paged
[DebuggerDisplay("{Owner}/{Name}#{Number} after={After}")]
internal sealed record PullRequestViewQueryVariables(
    [property: JsonPropertyName("o")] string Owner,
    [property: JsonPropertyName("r")] string Name,
    [property: JsonPropertyName("n")] int Number,
    [property: JsonPropertyName("after")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? After
);
