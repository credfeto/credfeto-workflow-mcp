using System;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Runs;

// Every member is nullable so an incomplete response is reported by the projector's own checks rather than failing the read
[DebuggerDisplay("{Id}: {Name} {Status} {Conclusion}")]
internal sealed record WorkflowRunResponse(
    [property: JsonPropertyName("id")] long? Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("conclusion")] string? Conclusion,
    [property: JsonPropertyName("head_sha")] string? HeadSha,
    [property: JsonPropertyName("head_branch")] string? HeadBranch,
    [property: JsonPropertyName("event")] string? Event,
    [property: JsonPropertyName("run_attempt")] int? RunAttempt,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt
);
