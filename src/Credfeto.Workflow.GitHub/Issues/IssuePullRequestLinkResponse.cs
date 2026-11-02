using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Issues;

[DebuggerDisplay("Url={Url}")]
internal sealed record IssuePullRequestLinkResponse([property: JsonPropertyName("url")] string? Url);
