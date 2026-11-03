using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Labels;

// A label that is not a standard one is sent without a colour or description, so GitHub picks the colour
[DebuggerDisplay("Name={Name}, Color={Color}")]
internal sealed record LabelCreateRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("color"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Color,
    [property: JsonPropertyName("description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Description
);
