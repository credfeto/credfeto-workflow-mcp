using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Search;

[DebuggerDisplay("Indices={Indices}")]
internal sealed record CodeSearchMatchResponse([property: JsonPropertyName("indices")] int[]? Indices);
