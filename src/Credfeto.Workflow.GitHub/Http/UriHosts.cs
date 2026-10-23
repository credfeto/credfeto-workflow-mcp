using System;
using System.Diagnostics.CodeAnalysis;

namespace Credfeto.Workflow.GitHub.Http;

internal static class UriHosts
{
    public static bool IsSameServer(Uri link, Uri? baseAddress)
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

    // Network-path references such as //other.host/path resolve to a different host, and rooted (/path) or
    // dot-segment (../path) references resolve outside the base path; resolving first and checking the result
    // catches all of them. The options validator guarantees the base path ends in '/', so the prefix match is
    // segment-aligned
    public static bool TryResolveUnderBasePath(Uri baseAddress, Uri relative, [NotNullWhen(true)] out Uri? resolved)
    {
        return Uri.TryCreate(baseUri: baseAddress, relativeUri: relative, out resolved)
            && IsSameServer(link: resolved, baseAddress: baseAddress)
            && resolved.AbsolutePath.StartsWith(
                value: baseAddress.AbsolutePath,
                comparisonType: StringComparison.Ordinal
            );
    }
}
