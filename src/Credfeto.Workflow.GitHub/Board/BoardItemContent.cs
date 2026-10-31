using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

// Draft issues match neither fragment and arrive as an empty object, so every member is nullable
[DebuggerDisplay("Number={Number}, Repository={Repository}")]
internal sealed record BoardItemContent(
    [property: JsonPropertyName("number")] int? Number,
    [property: JsonPropertyName("repository")] BoardItemRepository? Repository
);
