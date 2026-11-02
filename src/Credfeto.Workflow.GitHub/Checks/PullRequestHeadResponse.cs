using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Checks;

// Every member is nullable so an incomplete response is reported by the service's own checks rather than failing the read
[DebuggerDisplay("{Head} into {Base}")]
internal sealed record PullRequestHeadResponse(
    [property: JsonPropertyName("head")] PullRequestRef? Head,
    [property: JsonPropertyName("base")] PullRequestRef? Base
);
