using System;
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

    // No complete response was received, so there is no status code. A timeout can fire after GitHub has received
    // and applied the request, so the message must not claim it was not applied. The timeout or Polly exception is
    // kept only as the inner exception for server-side diagnostics
    public static HttpRequestException FromRejection(Exception exception)
    {
        return new(
            message: "GitHub request did not complete; it may or may not have been applied",
            inner: exception,
            statusCode: null
        );
    }
}
