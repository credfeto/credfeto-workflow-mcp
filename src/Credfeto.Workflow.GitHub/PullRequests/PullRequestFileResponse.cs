using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("FileName={FileName}")]
internal sealed record PullRequestFileResponse([property: JsonPropertyName("filename")] string? FileName);
