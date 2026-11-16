using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Http.LoggingExtensions;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Microsoft.Extensions.Logging;

namespace Credfeto.Workflow.GitHub.Http;

public sealed class GitHubRestClient : IGitHubRestClient
{
    // GitHub returns at most 100 items per page, so this allows 100,000 items before giving up
    public const int MaxPages = 1000;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubRestClient> _logger;

    public GitHubRestClient(IHttpClientFactory httpClientFactory, ILogger<GitHubRestClient> logger)
    {
        this._httpClientFactory = httpClientFactory;
        this._logger = logger;
    }

    public ValueTask<TResponse> GetAsync<TResponse>(
        string relativeUri,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken
    )
    {
        return this.ExchangeAsync(
            method: HttpMethod.Get,
            relativeUri: relativeUri,
            createContent: null,
            responseTypeInfo: responseTypeInfo,
            mediaType: null,
            cancellationToken: cancellationToken
        );
    }

    public ValueTask<TResponse> GetAsync<TResponse>(
        string relativeUri,
        string mediaType,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken
    )
    {
        return this.ExchangeAsync(
            method: HttpMethod.Get,
            relativeUri: relativeUri,
            createContent: null,
            responseTypeInfo: responseTypeInfo,
            mediaType: new MediaTypeWithQualityHeaderValue(mediaType),
            cancellationToken: cancellationToken
        );
    }

    public async ValueTask<string> GetTextAsync(
        string relativeUri,
        string mediaType,
        CancellationToken cancellationToken
    )
    {
        HttpClient httpClient = this.CreateHttpClient();
        Uri requestUri = ResolveOnConfiguredHost(
            baseAddress: GitHubBaseAddress.From(httpClient),
            relativeUri: relativeUri
        );

        GitHubHttpExchange exchange = await this.SendAsync(
            httpClient: httpClient,
            method: HttpMethod.Get,
            requestUri: requestUri,
            createContent: null,
            mediaType: new MediaTypeWithQualityHeaderValue(mediaType),
            cancellationToken: cancellationToken
        );

        using HttpResponseMessage response = exchange.Response;

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    // The redirect is never followed here: the primary handler surfaces it, so the caller's token is not sent to the
    // host the Location names, and the caller decides whether that host may be read
    public async ValueTask<Uri> GetRedirectLocationAsync(string relativeUri, CancellationToken cancellationToken)
    {
        HttpClient httpClient = this.CreateHttpClient();
        Uri requestUri = ResolveOnConfiguredHost(
            baseAddress: GitHubBaseAddress.From(httpClient),
            relativeUri: relativeUri
        );

        using HttpRequestMessage request = new(method: HttpMethod.Get, requestUri: requestUri);

        using HttpResponseMessage response = await GitHubHttpSender.SendAsync(
            httpClient: httpClient,
            request: request,
            cancellationToken: cancellationToken
        );

        if (response.StatusCode == HttpStatusCode.Found)
        {
            return response.Headers.Location is { IsAbsoluteUri: true } location
                ? location
                : throw new GitHubInvalidResponseException("GitHub returned a redirect with no absolute location");
        }

        if (response.IsSuccessStatusCode)
        {
            throw new GitHubInvalidResponseException("GitHub did not return a redirect");
        }

        throw await this.FailureAsync(response: response, requestUri: requestUri, cancellationToken: cancellationToken);
    }

    public ValueTask<TResponse> PostAsync<TRequest, TResponse>(
        string relativeUri,
        TRequest body,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken
    )
    {
        return this.ExchangeAsync(
            method: HttpMethod.Post,
            relativeUri: relativeUri,
            createContent: () => JsonContent.Create(inputValue: body, jsonTypeInfo: requestTypeInfo),
            responseTypeInfo: responseTypeInfo,
            mediaType: null,
            cancellationToken: cancellationToken
        );
    }

    public async ValueTask PostAsync<TRequest>(
        string relativeUri,
        TRequest body,
        JsonTypeInfo<TRequest> requestTypeInfo,
        CancellationToken cancellationToken
    )
    {
        HttpClient httpClient = this.CreateHttpClient();
        Uri requestUri = ResolveOnConfiguredHost(
            baseAddress: GitHubBaseAddress.From(httpClient),
            relativeUri: relativeUri
        );

        GitHubHttpExchange exchange = await this.SendAsync(
            httpClient: httpClient,
            method: HttpMethod.Post,
            requestUri: requestUri,
            createContent: () => JsonContent.Create(inputValue: body, jsonTypeInfo: requestTypeInfo),
            mediaType: null,
            cancellationToken: cancellationToken
        );

        exchange.Response.Dispose();
    }

    public ValueTask<TResponse> PatchAsync<TRequest, TResponse>(
        string relativeUri,
        TRequest body,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken
    )
    {
        return this.ExchangeAsync(
            method: HttpMethod.Patch,
            relativeUri: relativeUri,
            createContent: () => JsonContent.Create(inputValue: body, jsonTypeInfo: requestTypeInfo),
            responseTypeInfo: responseTypeInfo,
            mediaType: null,
            cancellationToken: cancellationToken
        );
    }

    public ValueTask<TResponse> DeleteAsync<TResponse>(
        string relativeUri,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken
    )
    {
        return this.ExchangeAsync(
            method: HttpMethod.Delete,
            relativeUri: relativeUri,
            createContent: null,
            responseTypeInfo: responseTypeInfo,
            mediaType: null,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<TResponse> ExchangeAsync<TResponse>(
        HttpMethod method,
        string relativeUri,
        Func<HttpContent>? createContent,
        JsonTypeInfo<TResponse> responseTypeInfo,
        MediaTypeWithQualityHeaderValue? mediaType,
        CancellationToken cancellationToken
    )
    {
        HttpClient httpClient = this.CreateHttpClient();
        Uri requestUri = ResolveOnConfiguredHost(
            baseAddress: GitHubBaseAddress.From(httpClient),
            relativeUri: relativeUri
        );

        GitHubHttpExchange exchange = await this.SendAsync(
            httpClient: httpClient,
            method: method,
            requestUri: requestUri,
            createContent: createContent,
            mediaType: mediaType,
            cancellationToken: cancellationToken
        );

        using HttpResponseMessage response = exchange.Response;

        return await ReadContentAsync(
            content: response.Content,
            typeInfo: responseTypeInfo,
            cancellationToken: cancellationToken
        );
    }

    public ValueTask<IReadOnlyList<TItem>> GetAllPagesAsync<TItem>(
        string relativeUri,
        JsonTypeInfo<TItem[]> pageTypeInfo,
        CancellationToken cancellationToken
    )
    {
        return this.GetAllPagesAsync<TItem[], TItem>(
            relativeUri: relativeUri,
            pageTypeInfo: pageTypeInfo,
            items: page => page,
            cancellationToken: cancellationToken
        );
    }

    public async ValueTask<IReadOnlyList<TItem>> GetAllPagesAsync<TPage, TItem>(
        string relativeUri,
        JsonTypeInfo<TPage> pageTypeInfo,
        Func<TPage, IReadOnlyList<TItem>?> items,
        CancellationToken cancellationToken
    )
    {
        HttpClient httpClient = this.CreateHttpClient();
        Uri baseAddress = GitHubBaseAddress.From(httpClient);
        List<TItem> collected = [];
        HashSet<Uri> fetched = [];
        Uri? requestUri = ResolveOnConfiguredHost(baseAddress: baseAddress, relativeUri: relativeUri);

        while (requestUri is not null)
        {
            EnsurePageMayBeFetched(fetched: fetched, requestUri: requestUri);

            GitHubHttpExchange exchange = await this.SendAsync(
                httpClient: httpClient,
                method: HttpMethod.Get,
                requestUri: requestUri,
                createContent: null,
                mediaType: null,
                cancellationToken: cancellationToken
            );

            using HttpResponseMessage response = exchange.Response;

            TPage page = await ReadContentAsync(
                content: response.Content,
                typeInfo: pageTypeInfo,
                cancellationToken: cancellationToken
            );

            collected.AddRange(
                items(page) ?? throw new GitHubInvalidResponseException("GitHub returned a page with no items list")
            );

            // A relative next link is resolved against the page that answered, which is not the one requested once a
            // redirect was followed. The link comes back already checked and is requested exactly as checked
            requestUri = GitHubLinkHeader.FindNextPage(
                headers: response.Headers,
                requestUri: exchange.RequestUri,
                baseAddress: baseAddress
            );
        }

        return collected;
    }

    // The next link comes from the upstream, so a repeated or endless chain must fail rather than loop forever.
    // Pages are keyed on the resolved request URI, so the same page reached through a differently written
    // reference is still recognised as a repeat
    private static void EnsurePageMayBeFetched(HashSet<Uri> fetched, Uri requestUri)
    {
        if (fetched.Count == MaxPages)
        {
            throw new GitHubInvalidResponseException($"GitHub pagination exceeded {MaxPages} pages");
        }

        if (!fetched.Add(requestUri))
        {
            throw new GitHubInvalidResponseException("GitHub pagination repeated a page that was already fetched");
        }
    }

    private HttpClient CreateHttpClient()
    {
        return this._httpClientFactory.CreateClient(GitHubSetup.HttpClientName);
    }

    // The caller owns and disposes the returned successful response; a failed one is disposed here. A request's
    // content can only be sent once, so each redirect hop is given new content from the factory
    private async ValueTask<GitHubHttpExchange> SendAsync(
        HttpClient httpClient,
        HttpMethod method,
        Uri requestUri,
        Func<HttpContent>? createContent,
        MediaTypeWithQualityHeaderValue? mediaType,
        CancellationToken cancellationToken
    )
    {
        GitHubHttpExchange exchange = await GitHubHttpRedirects.SendAsync(
            httpClient: httpClient,
            requestUri: requestUri,
            allowedBase: GitHubBaseAddress.From(httpClient),
            createRequest: hopUri =>
                CreateRequest(method: method, requestUri: hopUri, createContent: createContent, mediaType: mediaType),
            cancellationToken: cancellationToken
        );

        if (exchange.Response.IsSuccessStatusCode)
        {
            return exchange;
        }

        using (exchange.Response)
        {
            throw await this.FailureAsync(
                response: exchange.Response,
                requestUri: exchange.RequestUri,
                cancellationToken: cancellationToken
            );
        }
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        Uri requestUri,
        Func<HttpContent>? createContent,
        MediaTypeWithQualityHeaderValue? mediaType
    )
    {
        HttpRequestMessage request = new(method: method, requestUri: requestUri) { Content = createContent?.Invoke() };

        // A header set on the request is kept in place of the client's default, so this replaces the default media type
        if (mediaType is not null)
        {
            request.Headers.Accept.Add(mediaType);
        }

        return request;
    }

    private async ValueTask<HttpRequestException> FailureAsync(
        HttpResponseMessage response,
        Uri requestUri,
        CancellationToken cancellationToken
    )
    {
        this._logger.LogRequestFailed(path: requestUri.PathAndQuery, statusCode: (int)response.StatusCode);

        // Only a 403 is told apart by its message, and a 422 by its error codes, so any other failure is reported
        // without reading the body
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.UnprocessableEntity))
        {
            return GitHubHttpFailure.FromResponse(response);
        }

        GitHubErrorResponse? upstreamError = await GitHubHttpFailure.ReadUpstreamErrorAsync(
            content: response.Content,
            cancellationToken: cancellationToken
        );

        return GitHubHttpFailure.FromResponse(response: response, upstreamError: upstreamError);
    }

    private static ValueTask<TContent> ReadContentAsync<TContent>(
        HttpContent content,
        JsonTypeInfo<TContent> typeInfo,
        CancellationToken cancellationToken
    )
    {
        return GitHubJsonBody.ReadAsync(
            content: content,
            typeInfo: typeInfo,
            nullMessage: "GitHub returned a null response body",
            unreadableMessage: "GitHub returned a response body that could not be read as JSON",
            cancellationToken: cancellationToken
        );
    }

    // UriKind.Relative alone accepts references that resolve to a different host or outside the base path;
    // resolving here and checking the result keeps the caller's token on the configured API
    private static Uri ResolveOnConfiguredHost(Uri baseAddress, string relativeUri)
    {
        return UriHosts.TryResolveUnderBasePath(
            baseAddress: baseAddress,
            relative: new(uriString: relativeUri, uriKind: UriKind.Relative),
            out Uri? resolved
        )
            ? resolved
            : throw new UriFormatException(
                "Request URI must resolve under the configured GitHub API host and base path"
            );
    }
}
