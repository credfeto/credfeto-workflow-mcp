using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Labels;

[DebuggerDisplay("Name={Name}, Color={Color}")]
internal sealed record LabelResponse(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("color")] string? Color,
    [property: JsonPropertyName("description")] string? Description
);
