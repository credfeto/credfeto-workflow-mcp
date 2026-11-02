using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("Authors={Authors}")]
internal sealed record CommitAuthorsNode(
    [property: JsonPropertyName("authors")] GraphQlConnection<GitActorNode>? Authors
);
