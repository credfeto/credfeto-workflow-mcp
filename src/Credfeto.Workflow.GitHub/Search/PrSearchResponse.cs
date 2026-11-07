using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Search;

[DebuggerDisplay("TotalCount={TotalCount}, Items={Items}")]
internal sealed record PrSearchResponse(
    [property: JsonPropertyName("total_count")] int? TotalCount,
    [property: JsonPropertyName("items")] PrSearchItemResponse?[]? Items
);
