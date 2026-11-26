using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<PrTakeBackOutcome>))]
public enum PrTakeBackOutcome
{
    TakenBack,
    Refused,
    NotFound,
    NotTakenBack,
    MayHaveBeenTakenBack,
}
