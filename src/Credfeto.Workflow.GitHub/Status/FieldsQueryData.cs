using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Status;

[DebuggerDisplay("{Node}")]
internal sealed record FieldsQueryData([property: JsonPropertyName("node")] FieldsQueryProject? Node);
