using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

// Which kind of report a check was judged by: a check run, or a legacy commit status
[JsonConverter(typeof(JsonStringEnumConverter<PrCheckKind>))]
public enum PrCheckKind
{
    CheckRun,
    Status,
}
