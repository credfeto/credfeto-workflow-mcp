using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Branches;

[DebuggerDisplay("{Repository}")]
internal sealed record BranchRefsQueryData([property: JsonPropertyName("repository")] BranchRefsRepository? Repository);
