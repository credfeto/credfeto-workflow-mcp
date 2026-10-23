using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace Credfeto.Workflow.GitHub.Http;

public static partial class GitHubLinkHeader
{
    private const string NEXT_RELATION = "next";
    private const int MATCH_TIMEOUT_MILLISECONDS = 1000;

    public static string? FindNextPage(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues(name: "Link", out IEnumerable<string>? linkValues))
        {
            return null;
        }

        return linkValues
            .SelectMany(static linkValue => LinkPattern().Matches(linkValue))
            .Where(static match => IsNextRelation(match.Groups["rel"].Value))
            .Select(static match => ToRelativeUri(match.Groups["url"].Value))
            .FirstOrDefault();
    }

    private static bool IsNextRelation(string relations)
    {
        return relations
            .Split(separator: ' ', options: StringSplitOptions.RemoveEmptyEntries)
            .Contains(value: NEXT_RELATION, comparer: StringComparer.OrdinalIgnoreCase);
    }

    // GitHub advertises its own public host in Link even when the request went through a proxy at a
    // different ApiBaseUrl; following that absolute URL would bypass the configured host and send the
    // caller's token to it. Path and query resolve against the "GitHub" client's base address instead.
    private static string ToRelativeUri(string url)
    {
        return Uri.TryCreate(uriString: url, uriKind: UriKind.Absolute, out Uri? absolute)
            ? absolute.PathAndQuery
            : url;
    }

    [GeneratedRegex(
        pattern: "<(?<url>[^>]*)>[^<]*?rel=\"(?<rel>[^\"]*)\"",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MATCH_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex LinkPattern();
}
