using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
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

            // A relative next link is resolved against the absolute URI that actually answered, not the relative
            // one first asked for. The link comes back already checked and is requested exactly as checked
            requestUri = GitHubLinkHeader.FindNextPage(
                headers: response.Headers,
                requestUri: response.RequestMessage?.RequestUri ?? requestUri,
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

        HttpResponseMessage response = await httpClient.SendAsync(
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

    // The fixed message is what callers see; the JsonException, whose message can quote the upstream body, is
    // kept only as the inner exception for server-side diagnostics. The raw stream is read rather than using
    // ReadFromJsonAsync, which honours the Content-Type charset and throws InvalidOperationException (escaping
    // this wrapping) when the upstream sends an unknown one
    private static async ValueTask<TContent> ReadContentAsync<TContent>(
        HttpContent content,
        JsonTypeInfo<TContent> typeInfo,
        CancellationToken cancellationToken
    )
    {
        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);

        try
        {
            return await JsonSerializer.DeserializeAsync(
                    utf8Json: stream,
                    jsonTypeInfo: typeInfo,
                    cancellationToken: cancellationToken
                ) ?? throw new GitHubInvalidResponseException("GitHub returned a null response body");
        }
        catch (JsonException exception)
        {
            throw new GitHubInvalidResponseException(
                message: "GitHub returned a response body that could not be read as JSON",
                innerException: exception
            );
        }
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
