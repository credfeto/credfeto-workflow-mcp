using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Sent to the issues endpoint, which is the one that takes assignees; a field left null is not sent, so GitHub keeps it
[DebuggerDisplay("Title={Title}")]
internal sealed record PullRequestEditRequest(
    [property: JsonPropertyName("title"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Title,
    [property: JsonPropertyName("body"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Body,
    [property: JsonPropertyName("assignees"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? Assignees
);
