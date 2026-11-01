using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Status;

[DebuggerDisplay("{Fields}")]
internal sealed record FieldsQueryProject([property: JsonPropertyName("fields")] FieldsConnection? Fields);
