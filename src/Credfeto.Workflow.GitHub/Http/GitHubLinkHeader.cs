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

    public static string? FindNextPage(HttpResponseHeaders headers, Uri? baseAddress)
    {
        if (!headers.TryGetValues(name: "Link", out IEnumerable<string>? linkValues))
        {
            return null;
        }

        return linkValues
            .SelectMany(static linkValue => LinkPattern().Matches(linkValue))
            .Where(static match => IsNextRelation(match.Groups["rel"].Value))
            .Select(match => ToRelativeUri(url: match.Groups["url"].Value, baseAddress: baseAddress))
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
    // caller's token to it. The Link host is only compared, never requested: on the configured host the
    // path already carries any base path (e.g. GHE /api/v3/), so it stays rooted; on any other host the
    // path is made relative so it resolves under the configured base path.
    private static string ToRelativeUri(string url, Uri? baseAddress)
    {
        if (!Uri.TryCreate(uriString: url, uriKind: UriKind.Absolute, out Uri? absolute))
        {
            return url;
        }

        return IsSameServer(link: absolute, baseAddress: baseAddress)
            ? absolute.PathAndQuery
            : absolute.PathAndQuery[1..];
    }

    private static bool IsSameServer(Uri link, Uri? baseAddress)
    {
        return baseAddress is not null
            && Uri.Compare(
                uri1: link,
                uri2: baseAddress,
                partsToCompare: UriComponents.SchemeAndServer,
                compareFormat: UriFormat.Unescaped,
                comparisonType: StringComparison.OrdinalIgnoreCase
            ) == 0;
    }

    [GeneratedRegex(
        pattern: "<(?<url>[^>]*)>[^<]*?rel=\"(?<rel>[^\"]*)\"",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MATCH_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex LinkPattern();
}
