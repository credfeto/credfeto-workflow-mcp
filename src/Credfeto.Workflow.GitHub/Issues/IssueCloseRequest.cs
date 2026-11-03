using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Issues;

[DebuggerDisplay("State={State}, StateReason={StateReason}")]
internal sealed record IssueCloseRequest(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("state_reason")] string StateReason
);
