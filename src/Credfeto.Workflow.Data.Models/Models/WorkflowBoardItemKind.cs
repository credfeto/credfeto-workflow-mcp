using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<WorkflowBoardItemKind>))]
public enum WorkflowBoardItemKind
{
    Issue,
    PullRequest,
}
