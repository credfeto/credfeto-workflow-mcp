using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("Number={Number}, Title={Title}")]
internal sealed record PullRequestListItemResponse(
    [property: JsonPropertyName("number")] int? Number,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("draft")] bool? Draft,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("user")] PullRequestListUserResponse? User,
    [property: JsonPropertyName("head")] PullRequestListRefResponse? Head,
    [property: JsonPropertyName("base")] PullRequestListRefResponse? Base,
    [property: JsonPropertyName("labels")] PullRequestListLabelResponse?[]? Labels
);
