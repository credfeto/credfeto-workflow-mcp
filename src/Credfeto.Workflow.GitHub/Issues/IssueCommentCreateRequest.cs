using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Issues;

[DebuggerDisplay("Length={Body.Length}")]
internal sealed record IssueCommentCreateRequest([property: JsonPropertyName("body")] string Body);
