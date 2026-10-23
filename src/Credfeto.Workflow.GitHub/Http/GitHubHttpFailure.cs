using System.Net.Http;
using Polly;

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

    // No response was received, so there is no status code; the Polly exception is kept only as the inner
    // exception for server-side diagnostics
    public static HttpRequestException FromRejection(ExecutionRejectedException exception)
    {
        return new(
            message: "GitHub request was rejected before a response was received",
            inner: exception,
            statusCode: null
        );
    }
}
