using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Interfaces.Models;

// Errors stays a raw element so a malformed errors value is reported as an invalid response rather than
// failing the whole body, and so error entries without a string message still map to a description
[DebuggerDisplay("Data={Data}, Errors={Errors.ValueKind}")]
public sealed record GraphQlResponse<TData>(
    [property: JsonPropertyName("data")] TData? Data,
    [property: JsonPropertyName("errors")] JsonElement Errors
);
