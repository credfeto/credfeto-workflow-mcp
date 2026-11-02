using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Checks;

[DebuggerDisplay("{Id}")]
internal sealed record CheckRunApp([property: JsonPropertyName("id")] long? Id);
