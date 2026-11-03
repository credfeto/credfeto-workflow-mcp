using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("FullName={FullName}")]
internal sealed record PullRequestListRepositoryResponse([property: JsonPropertyName("full_name")] string? FullName);
