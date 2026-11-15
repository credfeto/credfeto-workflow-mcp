using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<WorkflowRunOutcome>))]
public enum WorkflowRunOutcome
{
    Success,
    Refused,
    NotFound,
    InvalidInput,
}
