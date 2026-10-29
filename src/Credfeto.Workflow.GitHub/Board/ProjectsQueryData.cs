using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

// GitHub returns a null repository, with an error, when the repository does not exist or the token cannot see it
[DebuggerDisplay("Repository={Repository}")]
internal sealed record ProjectsQueryData(
    [property: JsonPropertyName("repository")] ProjectsQueryRepository? Repository
);
