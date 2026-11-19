using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Auth;

// Every member is nullable because GitHub's answer is checked rather than trusted
[DebuggerDisplay("Core: {Core}, GraphQL: {Graphql}")]
internal sealed record RateLimitResourcesResponse(
    [property: JsonPropertyName("core")] RateLimitResourceResponse? Core,
    [property: JsonPropertyName("graphql")] RateLimitResourceResponse? Graphql
);
