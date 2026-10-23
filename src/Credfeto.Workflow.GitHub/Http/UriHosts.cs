using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Credfeto.Workflow.GitHub.Http;

internal static class UriHosts
{
    private static readonly FrozenSet<string> DotSegments = new[] { ".", ".." }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly SearchValues<char> PathSeparators = SearchValues.Create("/\\");

    public static bool IsSameServer(Uri link, Uri baseAddress)
    {
        return Uri.Compare(
                uri1: link,
                uri2: baseAddress,
                partsToCompare: UriComponents.SchemeAndServer,
                compareFormat: UriFormat.Unescaped,
                comparisonType: StringComparison.OrdinalIgnoreCase
            ) == 0;
    }

    // Network-path references such as //other.host/path resolve to a different host, and rooted (/path) or
    // dot-segment (../path) references resolve outside the base path; resolving first and checking the result
    // catches all of them. The options validator guarantees the base path ends in '/', so the prefix match is
    // segment-aligned
    public static bool TryResolveUnderBasePath(Uri baseAddress, Uri relative, [NotNullWhen(true)] out Uri? resolved)
    {
        return Uri.TryCreate(baseUri: baseAddress, relativeUri: relative, out resolved)
            && IsUnderBasePath(link: resolved, baseAddress: baseAddress);
    }

    public static bool IsUnderBasePath(Uri link, Uri baseAddress)
    {
        return IsSameServer(link: link, baseAddress: baseAddress)
            && link.AbsolutePath.StartsWith(value: baseAddress.AbsolutePath, comparisonType: StringComparison.Ordinal)
            && !HasEncodedPathTraversal(link);
    }

    // System.Uri leaves percent-encoded slashes (%2f, %5c) in place, so the prefix match above passes
    // repos%2f..%2f..%2fadmin, but a proxy that decodes and normalises the path resolves it outside the base path
    // with the caller's token. Any segment that decodes to a dot segment or contains a separator is rejected;
    // System.Uri already decodes and collapses %2e dot segments, so that half guards against a change in that
    private static bool HasEncodedPathTraversal(Uri link)
    {
        return link
            .Segments.Select(static segment => Uri.UnescapeDataString(segment.TrimEnd('/')))
            .Any(static segment => DotSegments.Contains(segment) || segment.AsSpan().ContainsAny(PathSeparators));
    }
}
