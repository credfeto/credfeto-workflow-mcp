using System.Net.Http;

namespace Credfeto.Workflow.GitHub.Http;

internal static class GitHubHttpFailure
{
    // The response body is deliberately left out: it is upstream content that callers may surface
    public static HttpRequestException FromResponse(HttpResponseMessage response)
    {
        return new(
            message: $"GitHub request failed with status {response.StatusCode}",
            inner: null,
            statusCode: response.StatusCode
        );
    }
}
