using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.ClosingIssues;

[DebuggerDisplay("Login={Login}")]
internal sealed record ClosingIssueOwner([property: JsonPropertyName("login")] string? Login);
