using System;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Search;

[DebuggerDisplay("MergedAt={MergedAt}")]
internal sealed record PrSearchPullRequestResponse([property: JsonPropertyName("merged_at")] DateTimeOffset? MergedAt);
