using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("Repository={Repository}")]
internal sealed record WorkflowStatusHistoryQueryData(
    [property: JsonPropertyName("repository")] WorkflowStatusHistoryRepositoryResponse? Repository
);
