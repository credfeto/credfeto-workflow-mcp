using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// GraphQL list items are nullable, so a node the token cannot read arrives as null rather than being left out
[DebuggerDisplay("TotalCount={TotalCount}, Nodes={Nodes.Count}, PageInfo={PageInfo}")]
internal sealed record GraphQlConnection<TNode>(
    [property: JsonPropertyName("totalCount")] int? TotalCount,
    [property: JsonPropertyName("pageInfo")] GraphQlPageInfo? PageInfo,
    [property: JsonPropertyName("nodes")] IReadOnlyList<TNode?>? Nodes
)
    where TNode : class;
