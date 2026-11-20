using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("{Repository}#{Number}")]
internal sealed record ClosingReferenceNode(
    [property: JsonPropertyName("number")] int? Number,
    [property: JsonPropertyName("repository")] ClosingReferenceRepository? Repository
);
