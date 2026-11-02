using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// User is null when the commit author's email does not match a GitHub account
[DebuggerDisplay("User={User}")]
internal sealed record GitActorNode([property: JsonPropertyName("user")] AuthorNode? User);
