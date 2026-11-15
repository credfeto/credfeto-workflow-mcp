using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("FileName={FileName} Status={Status}")]
internal sealed record PullRequestFileResponse(
    [property: JsonPropertyName("filename")] string? FileName,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("additions")] int? Additions,
    [property: JsonPropertyName("deletions")] int? Deletions
);
