using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

[JsonConverter(typeof(JsonStringEnumConverter<PrReviewCommentOutcome>))]
public enum PrReviewCommentOutcome
{
    Posted,
    Refused,
    NotFound,
    NotPosted,
    MayHaveBeenPosted,
}
