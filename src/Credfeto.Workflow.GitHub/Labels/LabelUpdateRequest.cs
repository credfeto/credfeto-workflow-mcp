using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Labels;

// GitHub matches label names in any case, so the new name puts back the standard spelling
[DebuggerDisplay("NewName={NewName}, Color={Color}")]
internal sealed record LabelUpdateRequest(
    [property: JsonPropertyName("new_name")] string NewName,
    [property: JsonPropertyName("color")] string Color,
    [property: JsonPropertyName("description")] string Description
);
