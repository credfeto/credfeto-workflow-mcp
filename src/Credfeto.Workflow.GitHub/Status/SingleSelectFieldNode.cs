using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Status;

[DebuggerDisplay("{Name} ({Id})")]
internal sealed record SingleSelectFieldNode(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name,
    // GraphQL list items are nullable, so an option the token cannot read arrives as null rather than being left out
    [property: JsonPropertyName("options")] IReadOnlyList<FieldOptionNode?>? Options
);
