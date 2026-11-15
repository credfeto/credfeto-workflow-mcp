using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<PrDiffMode>))]
public enum PrDiffMode
{
    Diff,
    NameOnly,
    Stat,
}
