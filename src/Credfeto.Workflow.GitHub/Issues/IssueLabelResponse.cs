using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Issues;

[DebuggerDisplay("Name={Name}")]
internal sealed record IssueLabelResponse([property: JsonPropertyName("name")] string? Name);
