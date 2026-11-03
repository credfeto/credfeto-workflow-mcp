using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Issues;

[DebuggerDisplay("Type={Type}")]
internal sealed record IssueTimelineSourceResponse(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("issue")] IssueTimelineSourceIssueResponse? Issue
);
