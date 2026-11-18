using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Repositories;

[DebuggerDisplay("{Repository}")]
internal sealed record RepoViewQueryData([property: JsonPropertyName("repository")] RepoViewRepository? Repository);
