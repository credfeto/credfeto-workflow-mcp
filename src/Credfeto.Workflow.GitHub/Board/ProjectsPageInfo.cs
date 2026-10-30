using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("HasNextPage={HasNextPage}, EndCursor={EndCursor}")]
internal sealed record ProjectsPageInfo(
    [property: JsonPropertyName("hasNextPage")] bool HasNextPage,
    [property: JsonPropertyName("endCursor")] string? EndCursor
);
