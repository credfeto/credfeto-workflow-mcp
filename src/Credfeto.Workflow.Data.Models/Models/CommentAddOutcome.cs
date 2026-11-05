using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<CommentAddOutcome>))]
public enum CommentAddOutcome
{
    Posted,
    Refused,
    NotPosted,
    MayHaveBeenPosted,
}
