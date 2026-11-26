using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

// Pending and NoChecks mean the checks were still so when the wait reached its limit, so the caller waits again
[JsonConverter(typeof(JsonStringEnumConverter<PrChecksWaitOutcome>))]
public enum PrChecksWaitOutcome
{
    Passed,
    Failed,
    Pending,
    NoChecks,
}
