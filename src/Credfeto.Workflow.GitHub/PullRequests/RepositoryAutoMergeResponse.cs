using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// GitHub only includes the setting for a caller who can administer the repository, so its absence means unknown
[DebuggerDisplay("AllowAutoMerge={AllowAutoMerge}")]
internal sealed record RepositoryAutoMergeResponse(
    [property: JsonPropertyName("allow_auto_merge")] bool? AllowAutoMerge
);
