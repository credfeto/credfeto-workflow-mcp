using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<RunLogOutcome>))]
public enum RunLogOutcome
{
    Log,
    Pending,
    NotFound,
}
