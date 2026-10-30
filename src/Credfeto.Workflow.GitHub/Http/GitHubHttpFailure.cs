using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Http;

internal static class GitHubHttpFailure
{
    private const string UPSTREAM_MESSAGE_KEY = "Credfeto.Workflow.GitHub.UpstreamMessage";

    // An error body is only read to tell failures with the same status apart, so anything larger is not an
    // error message GitHub would send and is ignored rather than buffered
    private const int MAX_UPSTREAM_MESSAGE_BYTES = 16 * 1024;

    public static HttpRequestException FromResponse(HttpResponseMessage response)
    {
        return FromResponse(response: response, upstreamMessage: null);
    }

    // The upstream message is kept only in the exception's Data, never in its Message: it is upstream content that
    // callers may surface, so it is there for readers that must tell failures with the same status apart
    public static HttpRequestException FromResponse(HttpResponseMessage response, string? upstreamMessage)
    {
        HttpRequestException exception = new(
            message: $"GitHub request failed with status {response.StatusCode}",
            inner: null,
            statusCode: response.StatusCode
        );

        if (upstreamMessage is not null)
        {
            exception.Data[UPSTREAM_MESSAGE_KEY] = upstreamMessage;
        }

        return exception;
    }

    public static string? UpstreamMessage(HttpRequestException exception)
    {
        return exception.Data[UPSTREAM_MESSAGE_KEY] as string;
    }

    // Content-Length is not trusted, so one byte past the limit is read to detect an oversize body. A body that is
    // empty, too large or not a GitHub error document has no usable message
    public static async ValueTask<string?> ReadUpstreamMessageAsync(
        HttpContent content,
        CancellationToken cancellationToken
    )
    {
        byte[] buffer = new byte[MAX_UPSTREAM_MESSAGE_BYTES + 1];

        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);

        int length = await stream.ReadAtLeastAsync(
            buffer: buffer,
            minimumBytes: buffer.Length,
            throwOnEndOfStream: false,
            cancellationToken: cancellationToken
        );

        if (length is 0 or > MAX_UPSTREAM_MESSAGE_BYTES)
        {
            return null;
        }

        try
        {
            return JsonSerializer
                .Deserialize(
                    utf8Json: buffer.AsSpan(start: 0, length: length),
                    jsonTypeInfo: GitHubJsonContext.Default.GitHubErrorResponse
                )
                ?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
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
