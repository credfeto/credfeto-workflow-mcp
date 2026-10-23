using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;

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
    // path is made relative so it resolves under the configured base path. Whatever the route, a result that
    // would not resolve under the configured host and base path is rejected here.
    private static string ToRelativeUri(string url, Uri? baseAddress)
    {
        string relativeUri = RewriteOntoBaseAddress(url: url, baseAddress: baseAddress);

        return baseAddress is null || ResolvesUnderBasePath(relativeUri: relativeUri, baseAddress: baseAddress)
            ? relativeUri
            : throw new GitHubInvalidResponseException(
                "GitHub returned a next page link outside the configured GitHub API host and base path"
            );
    }

    private static string RewriteOntoBaseAddress(string url, Uri? baseAddress)
    {
        Uri? absolute = ParseAbsoluteLink(url);

        if (absolute is null)
        {
            return url;
        }

        return UriHosts.IsSameServer(link: absolute, baseAddress: baseAddress)
            ? absolute.PathAndQuery
            : RelativeToBasePath(pathAndQuery: absolute.PathAndQuery, basePath: baseAddress?.AbsolutePath ?? "/");
    }

    // A link that resolves elsewhere (a same-host path outside the base path, a dot-segment relative link)
    // is upstream content, so it is an invalid response rather than the request-building failure the client
    // raises for a caller-supplied URI. A path stripped from another host can fail to parse as a relative
    // reference (a first segment containing ':' reads as a scheme or drive letter), so it cannot be mapped
    // onto the base path either
    private static bool ResolvesUnderBasePath(string relativeUri, Uri baseAddress)
    {
        return Uri.TryCreate(uriString: relativeUri, uriKind: UriKind.Relative, out Uri? relative)
            && UriHosts.TryResolveUnderBasePath(baseAddress: baseAddress, relative: relative, out _);
    }

    // The link is upstream content, so a malformed one is an invalid response rather than a request-building
    // failure; the fixed messages keep the upstream value out of what callers see. A network-path reference
    // such as //other.host/path parses as an absolute file URI, so the scheme check rejects it as well
    private static Uri? ParseAbsoluteLink(string url)
    {
        if (!IsRootedPath(url) && Uri.TryCreate(uriString: url, uriKind: UriKind.Absolute, out Uri? absolute))
        {
            if (!UriSchemes.IsHttp(absolute))
            {
                throw new GitHubInvalidResponseException("GitHub returned a next page link that is not an HTTP(S) URI");
            }

            return absolute;
        }

        return Uri.TryCreate(uriString: url, uriKind: UriKind.Relative, out _)
            ? null
            : throw new GitHubInvalidResponseException("GitHub returned a next page link that is not a valid URI");
    }

    // On Unix a rooted path such as /api/v3/items parses as an absolute file URI, which the scheme check would
    // reject; keeping it relative resolves it against the configured host on every OS. A network-path
    // reference (//host/path) is not a rooted path, so it still takes the absolute route and is rejected
    private static bool IsRootedPath(string url)
    {
        return url is ['/'] or ['/', not '/', ..];
    }

    // An upstream behind a proxy can carry the same base path as the configured one (GHE /api/v3/ behind a
    // proxy at /api/v3/); keeping it would double it once resolved. The options validator guarantees the base
    // path ends in '/', so the prefix match is segment-aligned, and a root base path strips only the leading '/'.
    private static string RelativeToBasePath(string pathAndQuery, string basePath)
    {
        return pathAndQuery.StartsWith(value: basePath, comparisonType: StringComparison.Ordinal)
            ? pathAndQuery[basePath.Length..]
            : pathAndQuery[1..];
    }

    [GeneratedRegex(
        pattern: "<(?<url>[^>]*)>[^<]*?rel=\"(?<rel>[^\"]*)\"",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MATCH_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex LinkPattern();
}
