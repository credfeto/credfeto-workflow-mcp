using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Status;

[DebuggerDisplay("{ProjectId}")]
internal sealed record FieldsQueryVariables([property: JsonPropertyName("projectId")] string ProjectId);
