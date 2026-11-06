using System.Diagnostics;
using System.Text.Json.Serialization;
using Credfeto.Workflow.GitHub.PullRequests;

namespace Credfeto.Workflow.GitHub.ClosingIssues;

[DebuggerDisplay("{Repository}#{Number}")]
internal sealed record ClosingIssueNode(
    [property: JsonPropertyName("number")] int? Number,
    [property: JsonPropertyName("repository")] ClosingIssueRepository? Repository,
    [property: JsonPropertyName("labels")] GraphQlConnection<LabelNode>? Labels
);
