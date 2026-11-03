using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Http;

// Errors is kept as raw JSON because its items are objects on most endpoints but plain strings on some, and a shape
// it does not expect must not cost the message
[DebuggerDisplay("{Message}")]
internal sealed record GitHubErrorResponse(
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("errors")] JsonElement? Errors
);
