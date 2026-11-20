using System.Text.Json.Serialization;

namespace Credfeto.Workflow.Data.Models.Models;

// How the head branch stands against the base, in the lower-case words the GitHub REST compare API uses
[JsonConverter(typeof(JsonStringEnumConverter<BranchCompareStatus>))]
public enum BranchCompareStatus
{
    [JsonStringEnumMemberName("identical")]
    Identical,

    [JsonStringEnumMemberName("ahead")]
    Ahead,

    [JsonStringEnumMemberName("behind")]
    Behind,

    [JsonStringEnumMemberName("diverged")]
    Diverged,
}
