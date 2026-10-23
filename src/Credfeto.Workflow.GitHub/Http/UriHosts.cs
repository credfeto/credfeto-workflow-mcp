using System;

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
}
