using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Every member is nullable so an incomplete node is reported by the reader's own checks rather than failing the page
[DebuggerDisplay("HasNextPage={HasNextPage}, EndCursor={EndCursor}")]
internal sealed record GraphQlPageInfo(
    [property: JsonPropertyName("hasNextPage")] bool? HasNextPage,
    [property: JsonPropertyName("endCursor")] string? EndCursor
);
