using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Auth;

// Every member is nullable because GitHub's answer is checked rather than trusted; Reset is in epoch seconds
[DebuggerDisplay("{Remaining}/{Limit} until {Reset}")]
internal sealed record RateLimitResourceResponse(
    [property: JsonPropertyName("remaining")] long? Remaining,
    [property: JsonPropertyName("limit")] long? Limit,
    [property: JsonPropertyName("reset")] long? Reset
);
