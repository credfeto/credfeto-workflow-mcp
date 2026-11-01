using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Leaks;

[DebuggerDisplay("Visibility={Visibility}")]
internal sealed record VisibilityQueryRepository([property: JsonPropertyName("visibility")] string? Visibility);
