using System;
using System.Globalization;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Issues;

namespace Credfeto.Workflow.GitHub.Runs;

// Upstream values are shown one per field, so each is kept to one printable line and an empty one is shown as '-'
internal static class RunText
{
    public const string NONE = "-";

    public static string Field(string? value)
    {
        return string.IsNullOrEmpty(value) ? NONE : UpstreamText.ReplaceLineBreaking(value);
    }

    public static string Timestamp(DateTimeOffset? value)
    {
        return value is { } timestamp
            ? timestamp.UtcDateTime.ToString(
                format: IssueCommentsProjector.TIMESTAMP_FORMAT,
                provider: CultureInfo.InvariantCulture
            )
            : NONE;
    }
}
