using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("{Title}")]
internal sealed record MilestoneNode([property: JsonPropertyName("title")] string? Title);
