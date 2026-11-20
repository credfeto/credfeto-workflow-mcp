using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("Contexts={Contexts}")]
internal sealed record StatusCheckRollupNode(
    [property: JsonPropertyName("contexts")] GraphQlConnection<CheckContextNode>? Contexts
);
