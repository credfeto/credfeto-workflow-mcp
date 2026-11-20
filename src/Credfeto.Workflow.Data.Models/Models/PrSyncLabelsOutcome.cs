using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<PrSyncLabelsOutcome>))]
public enum PrSyncLabelsOutcome
{
    Synced,
    Incomplete,
    Refused,
    Failed,
}
