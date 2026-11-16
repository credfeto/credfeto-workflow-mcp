using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Credfeto.Workflow.GitHub.Http;

// The primary handler never follows a redirect: it runs below CallerTokenForwardingHandler, so it would send the
// caller's token wherever the Location points. Each hop here is a fresh request through the whole pipeline, so the
// token is attached again, and it is only sent once its target is checked to lie under the allowed host and base path
public static class GitHubHttpRedirects
{
    public const int MaxRedirects = 5;

    private static readonly string TooManyRedirects = string.Create(
        provider: CultureInfo.InvariantCulture,
        $"GitHub redirected more than {MaxRedirects} times"
    );

    // The caller owns and disposes the returned response; every redirect response before it is disposed here
    internal static async ValueTask<GitHubHttpExchange> SendAsync(
        HttpClient httpClient,
        Uri requestUri,
        Uri allowedBase,
        Func<Uri, HttpRequestMessage> createRequest,
        CancellationToken cancellationToken
    )
    {
        int redirects = 0;

        while (true)
        {
            (HttpResponseMessage response, bool isRedirect) = await SendHopAsync(
                httpClient: httpClient,
                request: createRequest(requestUri),
                cancellationToken: cancellationToken
            );

            if (!isRedirect)
            {
                return new(Response: response, RequestUri: requestUri);
            }

            using (response)
            {
                if (redirects == MaxRedirects)
                {
                    throw GitHubHttpFailure.FromRedirect(response: response, reason: TooManyRedirects);
                }

                ++redirects;
                requestUri = ResolveLocation(response: response, requestUri: requestUri, allowedBase: allowedBase);
            }
        }
    }

    private static async ValueTask<(HttpResponseMessage Response, bool IsRedirect)> SendHopAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        using (request)
        {
            HttpResponseMessage response = await GitHubHttpSender.SendAsync(
                httpClient: httpClient,
                request: request,
                cancellationToken: cancellationToken
            );

            return (response, IsFollowed(statusCode: response.StatusCode, method: request.Method));
        }
    }

    // A method other than GET follows only 307 and 308, which keep the method and body. A 301, 302 or 303 would turn
    // it into a GET, and a mutation must reach GitHub at most once, so that redirect is returned as a failure
    private static bool IsFollowed(HttpStatusCode statusCode, HttpMethod method)
    {
        return statusCode switch
        {
            HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect => true,
            HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther => method
                == HttpMethod.Get,
            _ => false,
        };
    }

    // A relative Location is resolved against the request that returned it (RFC 9110 section 10.2.2). User info is
    // refused because the host comparison ignores it
    private static Uri ResolveLocation(HttpResponseMessage response, Uri requestUri, Uri allowedBase)
    {
        if (response.Headers.Location is not { } location)
        {
            throw GitHubHttpFailure.FromRedirect(
                response: response,
                reason: "GitHub returned a redirect with no location"
            );
        }

        return
            Uri.TryCreate(baseUri: requestUri, relativeUri: AsReference(location), out Uri? resolved)
            && string.IsNullOrEmpty(resolved.UserInfo)
            && UriHosts.IsUnderBasePath(link: resolved, baseAddress: allowedBase)
            ? resolved
            : throw GitHubHttpFailure.FromRedirect(
                response: response,
                reason: "GitHub redirected outside the configured GitHub API host and base path"
            );
    }

    // On Unix a rooted Location such as /api/v3/x can parse as an absolute file URI, which the host check would
    // reject; keeping it relative resolves it against the requested host on every OS. A network-path reference
    // (//host/path) is left as parsed, and is followed only when it resolves under the allowed host and base path
    private static Uri AsReference(Uri location)
    {
        return location.IsAbsoluteUri && location.IsFile && location.OriginalString is ['/'] or ['/', not '/', ..]
            ? new(uriString: location.OriginalString, uriKind: UriKind.Relative)
            : location;
    }
}
