using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Http;

internal static class GitHubHttpFailure
{
    private const string UPSTREAM_MESSAGE_KEY = "Credfeto.Workflow.GitHub.UpstreamMessage";

    private const string UPSTREAM_ERROR_CODES_KEY = "Credfeto.Workflow.GitHub.UpstreamErrorCodes";

    private const string CODE_PROPERTY = "code";

    // GitHub answers the rulesets endpoint with this 403 on a private repository whose plan has no rulesets, which
    // means no ruleset can require a check rather than that the caller lacks access
    private const string RULESETS_UNAVAILABLE_MESSAGE = "Upgrade to GitHub Pro or make this repository public";

    // An error body is only read to tell failures with the same status apart, so anything larger is not an
    // error message GitHub would send and is ignored rather than buffered
    private const int MAX_UPSTREAM_MESSAGE_BYTES = 16 * 1024;

    // The upstream message and error codes are kept only in the exception's Data, never in its Message: they are
    // upstream content that callers may surface, so they are there for readers that must tell failures with the same
    // status apart
    public static HttpRequestException FromResponse(
        HttpResponseMessage response,
        GitHubErrorResponse? upstreamError = null
    )
    {
        HttpRequestException exception = new(
            message: $"GitHub request failed with status {response.StatusCode}",
            inner: null,
            statusCode: response.StatusCode
        );

        if (upstreamError?.Message is { } upstreamMessage)
        {
            exception.Data[UPSTREAM_MESSAGE_KEY] = upstreamMessage;
        }

        string[] codes = ErrorCodes(upstreamError?.Errors);

        if (codes.Length != 0)
        {
            exception.Data[UPSTREAM_ERROR_CODES_KEY] = codes;
        }

        return exception;
    }

    public static string? UpstreamMessage(HttpRequestException exception)
    {
        return exception.Data[UPSTREAM_MESSAGE_KEY] as string;
    }

    // A validation failure names what went wrong in the code of each of its errors, such as already_exists
    public static bool HasErrorCode(HttpRequestException exception, string code)
    {
        return exception.Data[UPSTREAM_ERROR_CODES_KEY] is IReadOnlyList<string> codes
            && codes.Contains(value: code, comparer: StringComparer.Ordinal);
    }

    public static bool IsRulesetsUnavailable(HttpRequestException exception)
    {
        return exception.StatusCode == HttpStatusCode.Forbidden
            && UpstreamMessage(exception) is { } message
            && message.Contains(value: RULESETS_UNAVAILABLE_MESSAGE, comparisonType: StringComparison.Ordinal);
    }

    private static string[] ErrorCodes(JsonElement? errors)
    {
        if (errors is not { ValueKind: JsonValueKind.Array } items)
        {
            return [];
        }

        return
        [
            .. items
                .EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.Object)
                .Select(static item =>
                    item.TryGetProperty(propertyName: CODE_PROPERTY, value: out JsonElement code)
                    && code.ValueKind == JsonValueKind.String
                        ? code.GetString()
                        : null
                )
                .OfType<string>(),
        ];
    }

    // Content-Length is not trusted, so one byte past the limit is read to detect an oversize body. A body that is
    // empty, too large or not a GitHub error document has no usable message or error codes
    public static async ValueTask<GitHubErrorResponse?> ReadUpstreamErrorAsync(
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
            return JsonSerializer.Deserialize(
                utf8Json: buffer.AsSpan(start: 0, length: length),
                jsonTypeInfo: GitHubJsonContext.Default.GitHubErrorResponse
            );
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
