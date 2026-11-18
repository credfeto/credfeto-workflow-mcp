using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.ReleaseView;

// GitHub returns a null repository when the repository does not exist or the token cannot see it
[DebuggerDisplay("Repository={Repository}")]
internal sealed record ReleaseListQueryData(
    [property: JsonPropertyName("repository")] ReleaseListQueryRepository? Repository
);
