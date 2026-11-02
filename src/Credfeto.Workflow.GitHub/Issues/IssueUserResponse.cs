using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Issues;

[DebuggerDisplay("Login={Login}")]
internal sealed record IssueUserResponse([property: JsonPropertyName("login")] string? Login);
