using System.Diagnostics;
using System.Text.Json.Serialization;
using Credfeto.Workflow.GitHub.Issues;

namespace Credfeto.Workflow.GitHub.Search;

[DebuggerDisplay("Number={Number}, Title={Title}")]
internal sealed record IssueSearchItemResponse(
    [property: JsonPropertyName("number")] int? Number,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("labels")] IssueLabelResponse?[]? Labels
);
