using System;
using System.Net.Http;

namespace Credfeto.Workflow.GitHub.Http;

internal static class GitHubBaseAddress
{
    // GitHubSetup sets BaseAddress from GitHub:ApiBaseUrl, which the options validator requires at startup, so
    // this only fails for a client built outside that setup; everything after it can rely on a non-null base
    public static Uri From(HttpClient httpClient)
    {
        return httpClient.BaseAddress
            ?? throw new InvalidOperationException("The GitHub HTTP client has no configured base address");
    }
}
