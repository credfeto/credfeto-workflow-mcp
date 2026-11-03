using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Issues;

[DebuggerDisplay("Login={Login}, Type={Type}")]
internal sealed record IssueCommentUserResponse(
    [property: JsonPropertyName("login")] string? Login,
    [property: JsonPropertyName("type")] string? Type
);
