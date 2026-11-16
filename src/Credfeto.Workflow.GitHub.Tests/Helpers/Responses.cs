using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Credfeto.Workflow.GitHub.Tests.Helpers;

internal static class Responses
{
    public static HttpResponseMessage Json(string json, string? nextLink = null)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StringContent(content: json, encoding: Encoding.UTF8, mediaType: "application/json"),
        };

        if (nextLink is not null)
        {
            response.Headers.Add(name: "Link", value: nextLink);
        }

        return response;
    }

    public static HttpResponseMessage Html(string html)
    {
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(content: html, encoding: Encoding.UTF8, mediaType: "text/html"),
        };
    }

    public static HttpResponseMessage WithContentType(string body, string contentType)
    {
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(content: body, encoding: Encoding.UTF8)
            {
                Headers = { ContentType = MediaTypeHeaderValue.Parse(contentType) },
            },
        };
    }

    public static HttpResponseMessage Status(HttpStatusCode statusCode)
    {
        return new(statusCode);
    }

    // Set as text so the header is parsed as it would be off the wire
    public static HttpResponseMessage Redirect(HttpStatusCode statusCode, string location)
    {
        HttpResponseMessage response = new(statusCode);
        response.Headers.TryAddWithoutValidation(name: "Location", value: location);

        return response;
    }

    public static HttpResponseMessage StatusWithJson(HttpStatusCode statusCode, string json)
    {
        return new(statusCode)
        {
            Content = new StringContent(content: json, encoding: Encoding.UTF8, mediaType: "application/json"),
        };
    }

    public static HttpResponseMessage StatusWithRetryAfter(
        HttpStatusCode statusCode,
        RetryConditionHeaderValue retryAfter
    )
    {
        return new(statusCode) { Headers = { RetryAfter = retryAfter } };
    }

    public static HttpResponseMessage StatusWithRateLimitRemaining(HttpStatusCode statusCode, string remaining)
    {
        HttpResponseMessage response = new(statusCode);
        response.Headers.Add(name: "x-ratelimit-remaining", value: remaining);

        return response;
    }
}
