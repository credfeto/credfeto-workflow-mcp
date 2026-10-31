using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("NameWithOwner={NameWithOwner}")]
internal sealed record BoardItemRepository([property: JsonPropertyName("nameWithOwner")] string? NameWithOwner);
