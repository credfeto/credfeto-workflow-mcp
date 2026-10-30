using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("Repository={Repository}")]
internal sealed record PullRequestCommentsQueryData(
    [property: JsonPropertyName("repository")] PullRequestCommentsRepository? Repository
);
