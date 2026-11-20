using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Users, bots and mannequins have a login; teams have a slug instead
[DebuggerDisplay("{TypeName} {Login}{Slug}")]
internal sealed record RequestedReviewerNode(
    [property: JsonPropertyName("__typename")] string? TypeName,
    [property: JsonPropertyName("login")] string? Login,
    [property: JsonPropertyName("slug")] string? Slug
);
