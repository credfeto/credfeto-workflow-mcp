using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Models;

// Both members stay raw elements so errors are checked before data is bound to the caller's type: binding
// partial data of an unexpected shape first would fail the whole body and lose the error messages
[DebuggerDisplay("Data={Data.ValueKind}, Errors={Errors.ValueKind}")]
internal sealed record GraphQlResponse(
    [property: JsonPropertyName("data")] JsonElement Data,
    [property: JsonPropertyName("errors")] JsonElement Errors
);
