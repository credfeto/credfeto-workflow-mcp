using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Every member is nullable so an incomplete node is reported by the reader's own checks rather than failing the page
[DebuggerDisplay("{TypeName}/{Login}")]
internal sealed record AuthorNode(
    [property: JsonPropertyName("__typename")] string? TypeName,
    [property: JsonPropertyName("login")] string? Login
);
