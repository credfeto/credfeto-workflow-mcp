using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<RepoFileKind>))]
public enum RepoFileKind
{
    File,
    Directory,
    NotFound,
    Refused,
}
