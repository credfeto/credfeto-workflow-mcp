using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("{Oid}")]
internal sealed record CommitIdentityCommitNode(
    [property: JsonPropertyName("oid")] string? Oid,
    [property: JsonPropertyName("authors")] GraphQlConnection<CommitIdentityAuthorNode>? Authors
);
