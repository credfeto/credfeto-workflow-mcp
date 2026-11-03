using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("Login={Login}, Type={Type}")]
internal sealed record PullRequestListUserResponse(
    [property: JsonPropertyName("login")] string? Login,
    [property: JsonPropertyName("type")] string? Type
);
