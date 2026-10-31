using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("{Id}")]
internal sealed record GraphQlNode([property: JsonPropertyName("id")] string? Id);
