using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<BodyCheckOutcome>))]
public enum BodyCheckOutcome
{
    Passed,
    Refused,
}
