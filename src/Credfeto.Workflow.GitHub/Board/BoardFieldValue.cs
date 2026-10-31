using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

// A field value that is not single select matches no fragment and arrives as an empty object, so the name is null
[DebuggerDisplay("Name={Name}")]
internal sealed record BoardFieldValue([property: JsonPropertyName("name")] string? Name);
