using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Auth;

// Every member is nullable because GitHub's answer is checked rather than trusted
[DebuggerDisplay("Resources: {Resources}")]
internal sealed record RateLimitResponse(
    [property: JsonPropertyName("resources")] RateLimitResourcesResponse? Resources
);
