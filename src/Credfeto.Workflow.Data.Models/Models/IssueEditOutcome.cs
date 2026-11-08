using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<IssueEditOutcome>))]
public enum IssueEditOutcome
{
    Edited,
    Unchanged,
    Refused,
    NotFound,
    Failed,
}
