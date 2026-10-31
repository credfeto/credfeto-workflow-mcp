using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

// Every member is nullable so an incomplete node is reported by the reader's own checks rather than failing the page
[DebuggerDisplay("Type={Type}, Content={Content}, Legacy={Legacy}, Status={Status}")]
internal sealed record BoardItemNode(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("content")] BoardItemContent? Content,
    [property: JsonPropertyName("legacy")] BoardFieldValue? Legacy,
    [property: JsonPropertyName("status")] BoardFieldValue? Status
);
