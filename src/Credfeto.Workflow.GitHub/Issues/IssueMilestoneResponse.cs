using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Issues;

[DebuggerDisplay("Title={Title}")]
internal sealed record IssueMilestoneResponse([property: JsonPropertyName("title")] string? Title);
