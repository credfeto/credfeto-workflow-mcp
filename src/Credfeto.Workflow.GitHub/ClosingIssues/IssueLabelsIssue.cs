using System.Diagnostics;
using System.Text.Json.Serialization;
using Credfeto.Workflow.GitHub.PullRequests;

namespace Credfeto.Workflow.GitHub.ClosingIssues;

[DebuggerDisplay("Labels={Labels}")]
internal sealed record IssueLabelsIssue([property: JsonPropertyName("labels")] GraphQlConnection<LabelNode>? Labels);
