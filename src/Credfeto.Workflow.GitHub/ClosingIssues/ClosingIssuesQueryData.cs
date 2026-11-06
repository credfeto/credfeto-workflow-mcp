using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.ClosingIssues;

// Every member is nullable so an incomplete node is reported by the service's own checks rather than failing the page
[DebuggerDisplay("Repository={Repository}")]
internal sealed record ClosingIssuesQueryData(
    [property: JsonPropertyName("repository")] ClosingIssuesRepository? Repository
);
