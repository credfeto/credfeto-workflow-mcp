using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<LabelCreateOutcome>))]
public enum LabelCreateOutcome
{
    Created,
    Updated,
    Unchanged,
}
