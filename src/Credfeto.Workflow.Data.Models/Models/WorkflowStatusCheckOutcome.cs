using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<WorkflowStatusCheckOutcome>))]
public enum WorkflowStatusCheckOutcome
{
    Found,
    Unset,
    NotOnBoard,
}
