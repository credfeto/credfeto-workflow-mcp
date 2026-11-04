using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Search;

[DebuggerDisplay("Items={Items}")]
internal sealed record IssueSearchResponse([property: JsonPropertyName("items")] IssueSearchItemResponse?[]? Items);
