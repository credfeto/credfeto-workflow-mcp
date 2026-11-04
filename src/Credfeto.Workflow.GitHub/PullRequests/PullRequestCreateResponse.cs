using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("{HtmlUrl}")]
internal sealed record PullRequestCreateResponse([property: JsonPropertyName("html_url")] string? HtmlUrl);
