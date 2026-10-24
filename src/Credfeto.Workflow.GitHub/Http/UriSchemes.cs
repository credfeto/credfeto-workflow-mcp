using System;

namespace Credfeto.Workflow.GitHub.Http;

internal static class UriSchemes
{
    public static bool IsHttp(Uri uri)
    {
        return IsHttps(uri) || StringComparer.OrdinalIgnoreCase.Equals(x: uri.Scheme, y: Uri.UriSchemeHttp);
    }

    public static bool IsHttps(Uri uri)
    {
        return StringComparer.OrdinalIgnoreCase.Equals(x: uri.Scheme, y: Uri.UriSchemeHttps);
    }
}
