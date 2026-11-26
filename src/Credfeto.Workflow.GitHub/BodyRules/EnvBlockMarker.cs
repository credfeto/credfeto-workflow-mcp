using System;
using System.Text.RegularExpressions;

namespace Credfeto.Workflow.GitHub.BodyRules;

public static partial class EnvBlockMarker
{
    public const string ON_AN_ISSUE_RULE = "env-block marker on an issue";

    public const string MALFORMED_RULE = "malformed env-block marker";

    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    public static int? FindMarkerLine(string body)
    {
        return FindFirstLine(body: body, holdsMarker: static line => Marker().IsMatch(line));
    }

    // Exact markers are taken out first, so any marker text left on the line is one that is not exactly right
    public static int? FindMalformedMarkerLine(string body)
    {
        return FindFirstLine(body: body, holdsMarker: static line =>
            Marker().IsMatch(ExactMarker().Replace(input: line, replacement: string.Empty))
        );
    }

    private static int? FindFirstLine(string body, Func<string, bool> holdsMarker)
    {
        // Split on \n so each marker is reported on the line it is on, matching cfwf's line-by-line scan
        string[] lines = body.Split('\n');

        for (int index = 0; index < lines.Length; ++index)
        {
            if (holdsMarker(lines[index]))
            {
                return index + 1;
            }
        }

        return null;
    }

    [GeneratedRegex(
        pattern: "orchestrator:env-block",
        options: RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex Marker();

    [GeneratedRegex(
        pattern: "<!-- orchestrator:env-block image-sha=[0-9a-f]{40} -->",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex ExactMarker();
}
