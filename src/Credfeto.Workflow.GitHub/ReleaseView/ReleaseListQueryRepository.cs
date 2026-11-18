using System.Diagnostics;
using System.Text.Json.Serialization;
using Credfeto.Workflow.GitHub.PullRequests;

namespace Credfeto.Workflow.GitHub.ReleaseView;

[DebuggerDisplay("Releases={Releases}")]
internal sealed record ReleaseListQueryRepository(
    [property: JsonPropertyName("releases")] GraphQlConnection<ReleaseListNode>? Releases
);
