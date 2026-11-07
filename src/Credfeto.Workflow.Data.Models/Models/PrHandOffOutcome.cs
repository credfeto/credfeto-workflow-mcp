using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<PrHandOffOutcome>))]
public enum PrHandOffOutcome
{
    HandedOff,
    Refused,
    NotFound,
    NotHandedOff,
    MayHaveBeenHandedOff,
}
