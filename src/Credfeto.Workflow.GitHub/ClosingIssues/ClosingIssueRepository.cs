using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.ClosingIssues;

[DebuggerDisplay("{Owner}/{Name}")]
internal sealed record ClosingIssueRepository(
    [property: JsonPropertyName("owner")] ClosingIssueOwner? Owner,
    [property: JsonPropertyName("name")] string? Name
);
