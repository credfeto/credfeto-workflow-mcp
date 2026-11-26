using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<BlockOutcome>))]
public enum BlockOutcome
{
    Blocked,
    Refused,
    NotFound,
    NotPosted,
    MayHaveBeenPosted,
    LabelNotAdded,
}
