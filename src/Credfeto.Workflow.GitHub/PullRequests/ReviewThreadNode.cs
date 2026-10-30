using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Every member is nullable so an incomplete node is reported by the reader's own checks rather than failing the page
[DebuggerDisplay("{Path}:{Line} resolved={IsResolved}")]
internal sealed record ReviewThreadNode(
    [property: JsonPropertyName("isResolved")] bool? IsResolved,
    [property: JsonPropertyName("isOutdated")] bool? IsOutdated,
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("line")] int? Line,
    [property: JsonPropertyName("originalLine")] int? OriginalLine,
    [property: JsonPropertyName("comments")] GraphQlConnection<ReviewCommentNode>? Comments
);
