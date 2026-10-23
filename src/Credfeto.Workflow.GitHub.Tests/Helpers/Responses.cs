using System.Net;
using System.Net.Http;
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

    public static HttpResponseMessage Status(HttpStatusCode statusCode)
    {
        return new(statusCode);
    }
}
