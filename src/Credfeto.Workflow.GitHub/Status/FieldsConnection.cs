using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Status;

[DebuggerDisplay("{Nodes}")]
internal sealed record FieldsConnection(
    // GraphQL list items are nullable, so a node the token cannot read arrives as null rather than being left out
    [property: JsonPropertyName("nodes")] IReadOnlyList<SingleSelectFieldNode?>? Nodes
);
