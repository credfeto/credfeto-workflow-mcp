using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<PrHandOffAutoMerge>))]
public enum PrHandOffAutoMerge
{
    On,
    Unavailable,
    NotArmed,
}
