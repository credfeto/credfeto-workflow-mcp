using System;
using System.Collections.Generic;
using System.Net.Http;
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

    public async ValueTask<TResponse> GetAsync<TResponse>(
        string relativeUri,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken
    )
    {
        HttpClient httpClient = this.CreateHttpClient();
        Uri requestUri = ResolveOnConfiguredHost(
            baseAddress: GitHubBaseAddress.From(httpClient),
            relativeUri: relativeUri
        );

        using HttpResponseMessage response = await this.SendAsync(
            httpClient: httpClient,
            requestUri: requestUri,
            cancellationToken: cancellationToken
        );

        return await ReadContentAsync(
            content: response.Content,
            typeInfo: responseTypeInfo,
            cancellationToken: cancellationToken
        );
    }

    public async ValueTask<IReadOnlyList<TItem>> GetAllPagesAsync<TItem>(
        string relativeUri,
        JsonTypeInfo<TItem[]> pageTypeInfo,
        CancellationToken cancellationToken
    )
    {
        HttpClient httpClient = this.CreateHttpClient();
        Uri baseAddress = GitHubBaseAddress.From(httpClient);
        List<TItem> items = [];
        HashSet<Uri> fetched = [];
        Uri? requestUri = ResolveOnConfiguredHost(baseAddress: baseAddress, relativeUri: relativeUri);

        while (requestUri is not null)
        {
            EnsurePageMayBeFetched(fetched: fetched, requestUri: requestUri);

            using HttpResponseMessage response = await this.SendAsync(
                httpClient: httpClient,
                requestUri: requestUri,
                cancellationToken: cancellationToken
            );

            items.AddRange(
                await ReadContentAsync(
                    content: response.Content,
                    typeInfo: pageTypeInfo,
                    cancellationToken: cancellationToken
                )
            );

            // Redirects are not followed, so the page that answered is the one requested and a relative next link
            // is resolved against it. The link comes back already checked and is requested exactly as checked
            requestUri = GitHubLinkHeader.FindNextPage(
                headers: response.Headers,
                requestUri: requestUri,
                baseAddress: baseAddress
            );
        }

        return items;
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

    // The caller owns and disposes the returned successful response; a failed one is disposed here
    private async ValueTask<HttpResponseMessage> SendAsync(
        HttpClient httpClient,
        Uri requestUri,
        CancellationToken cancellationToken
    )
    {
        using HttpRequestMessage request = new(method: HttpMethod.Get, requestUri: requestUri);

        HttpResponseMessage response = await GitHubHttpSender.SendAsync(
            httpClient: httpClient,
            request: request,
            cancellationToken: cancellationToken
        );

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            this._logger.LogRequestFailed(path: requestUri.PathAndQuery, statusCode: (int)response.StatusCode);

            throw GitHubHttpFailure.FromResponse(response);
        }
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
