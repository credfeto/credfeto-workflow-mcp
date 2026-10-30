using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

// Every member is nullable so an incomplete node is reported by the resolver's own checks rather than failing the page
[DebuggerDisplay("{Title} #{Number} ({Id}) closed={Closed} {ResourcePath}")]
internal sealed record ProjectNode(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("number")] int? Number,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("closed")] bool? Closed,
    [property: JsonPropertyName("resourcePath")] string? ResourcePath
);
