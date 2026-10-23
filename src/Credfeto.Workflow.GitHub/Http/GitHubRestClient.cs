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

        Uri requestUri = ResolveOnConfiguredHost(baseAddress: httpClient.BaseAddress, relativeUri: relativeUri);

        using HttpRequestMessage request = new(method: HttpMethod.Get, requestUri: requestUri);

        using HttpResponseMessage response = await httpClient.SendAsync(
            request: request,
            cancellationToken: cancellationToken
        );

        if (!response.IsSuccessStatusCode)
        {
            this._logger.LogRequestFailed(path: relativeUri, statusCode: (int)response.StatusCode);
            throw GitHubHttpFailure.FromResponse(response);
        }

        TContent content = await ReadContentAsync(
            content: response.Content,
            typeInfo: typeInfo,
            cancellationToken: cancellationToken
        );

        return new(
            Content: content,
            NextUri: GitHubLinkHeader.FindNextPage(
                headers: response.Headers,
                requestUri: requestUri,
                baseAddress: httpClient.BaseAddress
            )
        );
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
    private static Uri ResolveOnConfiguredHost(Uri? baseAddress, string relativeUri)
    {
        Uri relative = new(uriString: relativeUri, uriKind: UriKind.Relative);

        if (
            baseAddress is null
            || !UriHosts.TryResolveUnderBasePath(baseAddress: baseAddress, relative: relative, out Uri? resolved)
        )
        {
            throw new UriFormatException("Request URI must resolve under the configured GitHub API host and base path");
        }

        return resolved;
    }
}
