using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// User is null when the commit author's email does not match a GitHub account
[DebuggerDisplay("{Email}")]
internal sealed record CommitIdentityAuthorNode(
    [property: JsonPropertyName("user")] AuthorNode? User,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("name")] string? Name
);
