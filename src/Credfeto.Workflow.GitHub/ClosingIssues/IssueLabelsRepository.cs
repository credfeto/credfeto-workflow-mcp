using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.ClosingIssues;

[DebuggerDisplay("Issue={Issue}")]
internal sealed record IssueLabelsRepository([property: JsonPropertyName("issue")] IssueLabelsIssue? Issue);
