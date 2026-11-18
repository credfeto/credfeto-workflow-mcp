using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.ReleaseView;

// Every member is nullable because GitHub's answer is checked rather than trusted; the release notes are never read
[DebuggerDisplay("{TagName}: {PublishedAt}, Draft={IsDraft}")]
internal sealed record ReleaseListNode(
    [property: JsonPropertyName("tagName")] string? TagName,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("isDraft")] bool? IsDraft,
    [property: JsonPropertyName("isPrerelease")] bool? IsPrerelease,
    [property: JsonPropertyName("isLatest")] bool? IsLatest,
    [property: JsonPropertyName("publishedAt")] string? PublishedAt
);
