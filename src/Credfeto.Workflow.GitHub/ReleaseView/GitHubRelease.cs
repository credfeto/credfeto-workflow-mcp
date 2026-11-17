using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.ReleaseView;

// Every member is nullable because GitHub's answer is checked rather than trusted; the release notes are never read
[DebuggerDisplay("{TagName}")]
internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string? TagName,
    [property: JsonPropertyName("published_at")] string? PublishedAt,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("name")] string? Name
);
