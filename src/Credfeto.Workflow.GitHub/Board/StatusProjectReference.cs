using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("Id={Id}")]
internal sealed record StatusProjectReference([property: JsonPropertyName("id")] string? Id);
