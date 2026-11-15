using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<PrDiffOutcome>))]
public enum PrDiffOutcome
{
    Success,
    NotFound,
    UsageError,
    RuntimeError,
}
