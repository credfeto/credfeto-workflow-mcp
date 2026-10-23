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

    public static string? FindNextPage(HttpResponseHeaders headers, Uri requestUri, Uri? baseAddress)
    {
        if (!headers.TryGetValues(name: "Link", out IEnumerable<string>? linkValues))
        {
            return null;
        }

        return linkValues
            .SelectMany(static linkValue => LinkPattern().Matches(linkValue))
            .Where(static match => IsNextRelation(match.Groups["rel"].Value))
            .Select(match =>
                ToRelativeUri(url: match.Groups["url"].Value, requestUri: requestUri, baseAddress: baseAddress)
            )
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
    // caller's token to it, so the Link host is only compared, never requested. Every link takes one route:
    // a link on another host is mapped under the configured base path, the result is resolved against the
    // URI of the request that returned it (RFC 8288 section 3.2, RFC 3986 section 5), so issues?page=2
    // returned for repos/o/r/issues names repos/o/r/issues?page=2, and it must then lie under the configured
    // host and base path. It is handed back relative to the base path, as the client expects; anything that
    // resolves elsewhere (a dot-segment climb, a rooted path outside the base path) is upstream content, so it
    // is an invalid response rather than the request-building failure the client raises for a caller URI
    private static string ToRelativeUri(string url, Uri requestUri, Uri? baseAddress)
    {
        Uri? absolute = ParseAbsoluteLink(url);

        if (baseAddress is null)
        {
            return absolute is null ? url : absolute.PathAndQuery[1..];
        }

        return
            ToReference(url: url, absolute: absolute, baseAddress: baseAddress) is { } reference
            && Uri.TryCreate(baseUri: requestUri, relativeUri: reference, out Uri? resolved)
            && UriHosts.IsUnderBasePath(link: resolved, baseAddress: baseAddress)
            ? resolved.PathAndQuery[baseAddress.AbsolutePath.Length..]
            : throw OutsideBasePath();
    }

    // A link on another host keeps only its path, mapped under the configured base path. A path stripped from
    // another host can fail to parse as a relative reference (a first segment containing ':' reads as a scheme
    // or drive letter), so it cannot be mapped onto the base path and is rejected
    private static Uri? ToReference(string url, Uri? absolute, Uri baseAddress)
    {
        if (absolute is null)
        {
            return new(uriString: url, uriKind: UriKind.Relative);
        }

        if (UriHosts.IsSameServer(link: absolute, baseAddress: baseAddress))
        {
            return absolute;
        }

        string pathUnderBase = RelativeToBasePath(
            pathAndQuery: absolute.PathAndQuery,
            basePath: baseAddress.AbsolutePath
        );

        return Uri.TryCreate(baseUri: baseAddress, relativeUri: pathUnderBase, out Uri? mapped) ? mapped : null;
    }

    private static GitHubInvalidResponseException OutsideBasePath()
    {
        return new("GitHub returned a next page link outside the configured GitHub API host and base path");
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

    // RFC 8288 section 3: a link-param is token BWS [ "=" BWS ( token / quoted-string ) ] with a
    // case-insensitive name, and only the first rel parameter counts, hence the lazy skip over earlier ones.
    // Anchoring every parameter name on ';' stops a name that merely ends in rel (foorel) being read as rel
    private const string TOKEN = "[!#$%&'*+.^_`|~0-9A-Za-z-]+";
    private const string QUOTED_STRING_CONTENT = "(?:[^\"\\\\]|\\\\.)*";
    private const string PARAMETER_SEPARATOR = @"\s*;\s*";
    private const string PARAMETER_ASSIGNMENT = @"\s*=\s*";

    private const string OTHER_PARAMETER =
        PARAMETER_SEPARATOR
        + TOKEN
        + "(?:"
        + PARAMETER_ASSIGNMENT
        + "(?:"
        + TOKEN
        + "|\""
        + QUOTED_STRING_CONTENT
        + "\"))?";

    private const string REL_PARAMETER =
        PARAMETER_SEPARATOR
        + "rel"
        + PARAMETER_ASSIGNMENT
        + "(?:\"(?<rel>"
        + QUOTED_STRING_CONTENT
        + ")\"|(?<rel>"
        + TOKEN
        + "))";

    private const string LINK_PATTERN = "<(?<url>[^>]*)>(?:" + OTHER_PARAMETER + ")*?" + REL_PARAMETER;

    [GeneratedRegex(
        pattern: LINK_PATTERN,
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: MATCH_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex LinkPattern();
}
