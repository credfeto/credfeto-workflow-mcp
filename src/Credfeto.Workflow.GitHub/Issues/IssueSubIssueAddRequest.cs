using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Issues;

[DebuggerDisplay("SubIssueId={SubIssueId}")]
internal sealed record IssueSubIssueAddRequest([property: JsonPropertyName("sub_issue_id")] long SubIssueId);
