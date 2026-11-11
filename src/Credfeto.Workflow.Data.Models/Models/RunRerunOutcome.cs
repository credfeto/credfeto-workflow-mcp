using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<RunRerunOutcome>))]
public enum RunRerunOutcome
{
    Success,
    Pending,
    Refused,
    NotFound,
}
