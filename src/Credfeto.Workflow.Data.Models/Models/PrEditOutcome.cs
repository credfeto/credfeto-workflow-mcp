using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<PrEditOutcome>))]
public enum PrEditOutcome
{
    Edited,
    Unchanged,
    Refused,
    NotFound,
    NotEdited,
    MayHaveBeenEdited,
}
