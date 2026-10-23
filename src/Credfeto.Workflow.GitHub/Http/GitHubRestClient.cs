using System;
using System.Collections.Generic;
using System.Net.Http;
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

    public async ValueTask<TResponse> GetAsync<TResponse>(
        string relativeUri,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken
    )
    {
        GitHubPage<TResponse> page = await this.GetPageAsync(
            relativeUri: relativeUri,
            typeInfo: responseTypeInfo,
            cancellationToken: cancellationToken
        );

        return page.Content;
    }

    public async ValueTask<IReadOnlyList<TItem>> GetAllPagesAsync<TItem>(
        string relativeUri,
        JsonTypeInfo<TItem[]> pageTypeInfo,
        CancellationToken cancellationToken
    )
    {
        List<TItem> items = [];
        HashSet<string> fetched = [with(StringComparer.Ordinal)];
        string? nextUri = relativeUri;

        while (nextUri is not null)
        {
            EnsurePageMayBeFetched(fetched: fetched, nextUri: nextUri);

            GitHubPage<TItem[]> page = await this.GetPageAsync(
                relativeUri: nextUri,
                typeInfo: pageTypeInfo,
                cancellationToken: cancellationToken
            );

            items.AddRange(page.Content);
            nextUri = page.NextUri;
        }

        return items;
    }

    // The next link comes from the upstream, so a repeated or endless chain must fail rather than loop forever
    private static void EnsurePageMayBeFetched(HashSet<string> fetched, string nextUri)
    {
        if (fetched.Count == MaxPages)
        {
            throw new GitHubInvalidResponseException($"GitHub pagination exceeded {MaxPages} pages");
        }

        if (!fetched.Add(nextUri))
        {
            throw new GitHubInvalidResponseException("GitHub pagination repeated a page that was already fetched");
        }
    }

    private async ValueTask<GitHubPage<TContent>> GetPageAsync<TContent>(
        string relativeUri,
        JsonTypeInfo<TContent> typeInfo,
        CancellationToken cancellationToken
    )
    {
        HttpClient httpClient = this._httpClientFactory.CreateClient(GitHubSetup.HttpClientName);

        using HttpRequestMessage request = new(
            method: HttpMethod.Get,
            requestUri: ResolveOnConfiguredHost(baseAddress: httpClient.BaseAddress, relativeUri: relativeUri)
        );

        using HttpResponseMessage response = await httpClient.SendAsync(
            request: request,
            cancellationToken: cancellationToken
        );

        if (!response.IsSuccessStatusCode)
        {
            this._logger.LogRequestFailed(path: relativeUri, statusCode: (int)response.StatusCode);
            throw GitHubHttpFailure.FromResponse(response);
        }

        TContent content =
            await response.Content.ReadFromJsonAsync(jsonTypeInfo: typeInfo, cancellationToken: cancellationToken)
            ?? throw new GitHubInvalidResponseException("GitHub returned a null response body");

        return new(
            Content: content,
            NextUri: GitHubLinkHeader.FindNextPage(headers: response.Headers, baseAddress: httpClient.BaseAddress)
        );
    }

    // UriKind.Relative alone accepts network-path references such as //other.host/path, which resolve to
    // a different host; resolving here and checking the result keeps the caller's token on the configured host
    private static Uri ResolveOnConfiguredHost(Uri? baseAddress, string relativeUri)
    {
        Uri relative = new(uriString: relativeUri, uriKind: UriKind.Relative);

        if (
            baseAddress is null
            || !Uri.TryCreate(baseUri: baseAddress, relativeUri: relative, out Uri? resolved)
            || !UriHosts.IsSameServer(link: resolved, baseAddress: baseAddress)
        )
        {
            throw new UriFormatException("Request URI must resolve to the configured GitHub API host");
        }

        return resolved;
    }
}
