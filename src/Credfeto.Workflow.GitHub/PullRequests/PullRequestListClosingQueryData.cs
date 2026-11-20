using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Each pull request is read under its own alias (p<number>), so the repository is a map of alias to pull request
[DebuggerDisplay("Repository={Repository}")]
internal sealed record PullRequestListClosingQueryData(
    [property: JsonPropertyName("repository")] Dictionary<string, PullRequestListClosingNode?>? Repository
);
