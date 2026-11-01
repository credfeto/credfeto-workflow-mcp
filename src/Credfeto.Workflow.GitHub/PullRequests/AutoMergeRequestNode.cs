using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Its presence alone means auto-merge is on; the member exists because GraphQL needs a selection on the object
[DebuggerDisplay("EnabledAt={EnabledAt}")]
internal sealed record AutoMergeRequestNode([property: JsonPropertyName("enabledAt")] string? EnabledAt);
