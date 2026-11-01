using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("Repository={Repository}")]
internal sealed record PullRequestViewQueryData(
    [property: JsonPropertyName("repository")] PullRequestViewRepository? Repository
);
