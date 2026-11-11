using System.Diagnostics;
using System.Text.Json.Serialization;
using Credfeto.Workflow.GitHub.Checks;

namespace Credfeto.Workflow.GitHub.Runs;

// Every member is nullable so an incomplete response is reported by the service's own checks rather than failing the read
[DebuggerDisplay("#{Number} {State}: {Head} into {Base}")]
internal sealed record RunPullRequestResponse(
    [property: JsonPropertyName("number")] int? Number,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("head")] PullRequestRef? Head,
    [property: JsonPropertyName("base")] PullRequestRef? Base
);
