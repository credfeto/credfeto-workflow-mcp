using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// StartLine and StartSide are left null, and so not sent, for a comment on a single line
[DebuggerDisplay("{Path}:{Line} {Side}")]
internal sealed record PullRequestReviewCommentCreateRequest(
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("commit_id")] string CommitId,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("start_line"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        int? StartLine,
    [property: JsonPropertyName("start_side"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? StartSide
);
