using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

// GraphQL list items are nullable, so a node the token cannot read arrives as null rather than being left out
[DebuggerDisplay("Nodes={Nodes.Count}, PageInfo={PageInfo}")]
internal sealed record ProjectsConnection(
    [property: JsonPropertyName("nodes")] IReadOnlyList<ProjectNode?>? Nodes,
    [property: JsonPropertyName("pageInfo")] ProjectsPageInfo? PageInfo
);
