using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Repositories;

[DebuggerDisplay("{Owner}/{Name}")]
internal sealed record RepoViewQueryVariables(
    [property: JsonPropertyName("o")] string Owner,
    [property: JsonPropertyName("r")] string Name
);
