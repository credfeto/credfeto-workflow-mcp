using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<PrReplyOutcome>))]
public enum PrReplyOutcome
{
    Posted,
    Refused,
    NotFound,
    NotPosted,
    MayHaveBeenPosted,
}
