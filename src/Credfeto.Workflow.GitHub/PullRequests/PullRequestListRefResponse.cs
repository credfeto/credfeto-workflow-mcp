using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("Ref={Ref}")]
internal sealed record PullRequestListRefResponse(
    [property: JsonPropertyName("ref")] string? Ref,
    [property: JsonPropertyName("repo")] PullRequestListRepositoryResponse? Repository
);
