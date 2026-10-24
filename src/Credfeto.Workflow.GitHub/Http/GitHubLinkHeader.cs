using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;

namespace Credfeto.Workflow.GitHub.Http;

public static class GitHubLinkHeader
{
    private const string NEXT_RELATION = "next";
    private const string RELATION_PARAMETER = "rel";
    private const char LINK_SEPARATOR = ',';
    private const char PARAMETER_SEPARATOR = ';';
    private const char NOT_DELIMITED = '\0';

    // RFC 8288 section 3: a Link header is a comma-separated list of link-values, each a <URI-Reference>
    // followed by ';'-separated link-params of the form token [ "=" ( token / quoted-string ) ]. Each URI and
    // parameter is read only from the start of its link-value or parameter, so a '<...>; rel=next' sequence
    // inside a quoted string or a URI is never read as a link or a relation. Only the first rel parameter counts
    public static Uri? FindNextPage(HttpResponseHeaders headers, Uri requestUri, Uri baseAddress)
    {
        if (!headers.TryGetValues(name: "Link", out IEnumerable<string>? linkValues))
        {
            return null;
        }

        return linkValues
            .SelectMany(static linkValue => SplitOutsideDelimiters(text: linkValue, separator: LINK_SEPARATOR))
            .Select(static link => SplitOutsideDelimiters(text: link, separator: PARAMETER_SEPARATOR))
            .Where(static link => IsNextRelation(FirstRelation(link.Skip(1))))
            .Select(static link => link[0].Trim())
            .Where(IsUriReference)
            .Select(target => ResolveNextPage(url: target[1..^1], requestUri: requestUri, baseAddress: baseAddress))
            .FirstOrDefault();
    }

    // Splits only where the separator is outside a <URI-Reference> and outside a quoted-string, skipping the
    // character after a backslash escape in a quoted-string. An unterminated '<' or '"' runs to the end of the
    // text, so nothing after it is read as a link or a parameter
    private static List<string> SplitOutsideDelimiters(string text, char separator)
    {
        List<string> parts = [];
        int start = 0;
        char closing = NOT_DELIMITED;
        bool escaped = false;

        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];

            if (escaped)
            {
                escaped = false;
            }
            else if (closing == NOT_DELIMITED)
            {
                if (current == separator)
                {
                    parts.Add(text[start..index]);
                    start = index + 1;
                }
                else
                {
                    closing = current switch
                    {
                        '<' => '>',
                        '"' => '"',
                        _ => NOT_DELIMITED,
                    };
                }
            }
            else if (current == closing)
            {
                closing = NOT_DELIMITED;
            }
            else
            {
                escaped = closing == '"' && current == '\\';
            }
        }

        parts.Add(text[start..]);

        return parts;
    }

    // The relation value is compared as written, without unescaping: a relation type is a token or a URI
    // (RFC 8288 section 3.3), neither of which can contain a backslash, so an escaped value is never next
    private static string FirstRelation(IEnumerable<string> parameters)
    {
        return parameters
            .Select(static parameter => parameter.Split(separator: '=', count: 2))
            .Where(static parameter =>
                parameter.Length == 2
                && StringComparer.OrdinalIgnoreCase.Equals(x: parameter[0].Trim(), y: RELATION_PARAMETER)
            )
            .Select(static parameter => Unquote(parameter[1].Trim()))
            .FirstOrDefault(defaultValue: string.Empty);
    }

    private static string Unquote(string value)
    {
        return value is ['"', .. string quoted, '"'] ? quoted : value;
    }

    // The first '>' closes the URI-Reference, so anything after it makes the link-value malformed
    private static bool IsUriReference(string target)
    {
        return target is ['<', ..] && target.IndexOf('>', StringComparison.Ordinal) == target.Length - 1;
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
    // host and base path. The checked absolute URI is handed back to be requested exactly as checked; anything
    // that resolves elsewhere (a dot-segment climb, a rooted path outside the base path) is upstream content, so
    // it is an invalid response rather than the request-building failure the client raises for a caller URI
    private static Uri ResolveNextPage(string url, Uri requestUri, Uri baseAddress)
    {
        return
            ToReference(url: url, absolute: ParseAbsoluteLink(url), baseAddress: baseAddress) is { } reference
            && Uri.TryCreate(baseUri: requestUri, relativeUri: reference, out Uri? resolved)
            && UriHosts.IsUnderBasePath(link: resolved, baseAddress: baseAddress)
            ? resolved
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
}
