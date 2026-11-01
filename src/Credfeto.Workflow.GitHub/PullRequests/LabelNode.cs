using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("Name={Name}")]
internal sealed record LabelNode([property: JsonPropertyName("name")] string? Name);
