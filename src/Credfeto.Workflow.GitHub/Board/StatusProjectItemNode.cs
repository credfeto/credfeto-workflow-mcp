using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

// Every member is nullable so an incomplete node is reported by the reader's own checks rather than failing the page
[DebuggerDisplay("Project={Project}, Legacy={Legacy}, Status={Status}")]
internal sealed record StatusProjectItemNode(
    [property: JsonPropertyName("project")] StatusProjectReference? Project,
    [property: JsonPropertyName("legacy")] BoardFieldValue? Legacy,
    [property: JsonPropertyName("status")] BoardFieldValue? Status
);
