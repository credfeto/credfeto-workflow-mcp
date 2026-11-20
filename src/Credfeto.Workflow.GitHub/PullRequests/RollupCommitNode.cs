using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// The rollup is null when the commit has no checks or statuses at all
[DebuggerDisplay("StatusCheckRollup={StatusCheckRollup}")]
internal sealed record RollupCommitNode(
    [property: JsonPropertyName("statusCheckRollup")] StatusCheckRollupNode? StatusCheckRollup
);
