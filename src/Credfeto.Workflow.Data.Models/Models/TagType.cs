using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

// Written in lower case, the same words cfwf tag view prints
[JsonConverter(typeof(JsonStringEnumConverter<TagType>))]
public enum TagType
{
    [JsonStringEnumMemberName("lightweight")]
    Lightweight,

    [JsonStringEnumMemberName("annotated")]
    Annotated,
}
