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
        string? nextUri = relativeUri;

        while (nextUri is not null)
        {
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

    private async ValueTask<GitHubPage<TContent>> GetPageAsync<TContent>(
        string relativeUri,
        JsonTypeInfo<TContent> typeInfo,
        CancellationToken cancellationToken
    )
    {
        HttpClient httpClient = this._httpClientFactory.CreateClient(GitHubSetup.HttpClientName);

        // UriKind.Relative rejects absolute URIs so the caller's token can only reach the configured host
        using HttpRequestMessage request = new(
            method: HttpMethod.Get,
            requestUri: new Uri(uriString: relativeUri, uriKind: UriKind.Relative)
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

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        TContent content =
            await JsonSerializer.DeserializeAsync(
                utf8Json: stream,
                jsonTypeInfo: typeInfo,
                cancellationToken: cancellationToken
            ) ?? throw new GitHubInvalidResponseException("GitHub returned a null response body");

        return new(
            Content: content,
            NextUri: GitHubLinkHeader.FindNextPage(headers: response.Headers, baseAddress: httpClient.BaseAddress)
        );
    }
}
