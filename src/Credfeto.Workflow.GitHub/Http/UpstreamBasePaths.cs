using System;
using System.Diagnostics.CodeAnalysis;

namespace Credfeto.Workflow.GitHub.Http;

// The upstream base path is a path on a host the server never contacts, so it is parsed against a reserved
// placeholder origin (RFC 2606 .invalid) purely to read it back as a URI path
internal static class UpstreamBasePaths
{
    private const string PLACEHOLDER_ORIGIN = "https://upstream.invalid";

    public static bool TryCreate(string upstreamBasePath, [NotNullWhen(true)] out Uri? upstream)
    {
        return Uri.TryCreate(uriString: PLACEHOLDER_ORIGIN + upstreamBasePath, uriKind: UriKind.Absolute, out upstream);
    }

    public static Uri Create(string upstreamBasePath)
    {
        return new(PLACEHOLDER_ORIGIN + upstreamBasePath);
    }
}
