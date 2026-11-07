using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Search;

// Only the match positions are read; the matched fragment is file content and is never returned to the caller
[DebuggerDisplay("Matches={Matches}")]
internal sealed record CodeSearchTextMatchResponse(
    [property: JsonPropertyName("matches")] CodeSearchMatchResponse?[]? Matches
);
