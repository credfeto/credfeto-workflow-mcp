using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Repositories;

[DebuggerDisplay("Login={Login}, Role={RoleName}")]
internal sealed record RepoCollaboratorResponse(
    [property: JsonPropertyName("login")] string? Login,
    [property: JsonPropertyName("role_name")] string? RoleName
);
