using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Issues;

[DebuggerDisplay("State={State}")]
internal sealed record IssueReopenRequest([property: JsonPropertyName("state")] string State);
