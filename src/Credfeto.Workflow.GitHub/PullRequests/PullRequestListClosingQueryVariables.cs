using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("{Owner}/{Name}")]
internal sealed record PullRequestListClosingQueryVariables(
    [property: JsonPropertyName("o")] string Owner,
    [property: JsonPropertyName("r")] string Name
);
