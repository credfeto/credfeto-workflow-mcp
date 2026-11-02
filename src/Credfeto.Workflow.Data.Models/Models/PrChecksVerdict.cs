using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<PrChecksVerdict>))]
public enum PrChecksVerdict
{
    Passed,
    Failed,
    Pending,
    NoChecks,
}
