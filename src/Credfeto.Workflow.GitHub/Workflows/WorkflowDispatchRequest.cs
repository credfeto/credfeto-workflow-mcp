using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Workflows;

[DebuggerDisplay("{Ref}")]
internal sealed record WorkflowDispatchRequest(
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("inputs")] IReadOnlyDictionary<string, string> Inputs
);
