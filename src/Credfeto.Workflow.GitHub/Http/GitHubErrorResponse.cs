using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Http;

[DebuggerDisplay("{Message}")]
internal sealed record GitHubErrorResponse([property: JsonPropertyName("message")] string? Message);
