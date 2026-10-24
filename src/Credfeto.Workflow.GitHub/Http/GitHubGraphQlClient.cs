using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Http.LoggingExtensions;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Microsoft.Extensions.Logging;

namespace Credfeto.Workflow.GitHub.Http;

public sealed class GitHubGraphQlClient : IGitHubGraphQlClient
{
    private const string UNKNOWN_ERROR = "unknown error";

    private const string ENTERPRISE_REST_PATH_SUFFIX = "/api/v3/";

    private static readonly Uri GraphQlUri = new(uriString: "graphql", uriKind: UriKind.Relative);

    // GitHub Enterprise Server serves REST under /api/v3/ but GraphQL at the sibling /api/graphql
    private static readonly Uri EnterpriseGraphQlUri = new(uriString: "../graphql", uriKind: UriKind.Relative);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubGraphQlClient> _logger;

    public GitHubGraphQlClient(IHttpClientFactory httpClientFactory, ILogger<GitHubGraphQlClient> logger)
    {
        this._httpClientFactory = httpClientFactory;
        this._logger = logger;
    }

    public async ValueTask<TData> ExecuteAsync<TVariables, TData>(
        string query,
        TVariables variables,
        JsonTypeInfo<TVariables> variablesTypeInfo,
        JsonTypeInfo<GraphQlResponse<TData>> responseTypeInfo,
        CancellationToken cancellationToken
    )
    {
        HttpClient httpClient = this._httpClientFactory.CreateClient(GitHubSetup.HttpClientName);

        using HttpRequestMessage request = new(
            method: HttpMethod.Post,
            requestUri: ResolveGraphQlUri(GitHubBaseAddress.From(httpClient))
        );
        request.Content = await BuildRequestContentAsync(
            query: query,
            variables: variables,
            variablesTypeInfo: variablesTypeInfo,
            cancellationToken: cancellationToken
        );

        using HttpResponseMessage response = await GitHubHttpSender.SendAsync(
            httpClient: httpClient,
            request: request,
            cancellationToken: cancellationToken
        );

        if (!response.IsSuccessStatusCode)
        {
            this._logger.LogRequestFailed((int)response.StatusCode);
            throw GitHubHttpFailure.FromResponse(response);
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        GraphQlResponse<TData> graphQlResponse = await ReadResponseAsync(
            stream: stream,
            responseTypeInfo: responseTypeInfo,
            cancellationToken: cancellationToken
        );

        return ReadData(graphQlResponse);
    }

    // The fixed message is what callers see; the JsonException, whose message can quote the upstream body, is
    // kept only as the inner exception for server-side diagnostics
    private static async ValueTask<GraphQlResponse<TData>> ReadResponseAsync<TData>(
        Stream stream,
        JsonTypeInfo<GraphQlResponse<TData>> responseTypeInfo,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await JsonSerializer.DeserializeAsync(
                    utf8Json: stream,
                    jsonTypeInfo: responseTypeInfo,
                    cancellationToken: cancellationToken
                ) ?? throw new GitHubInvalidResponseException("GitHub GraphQL response body was null");
        }
        catch (JsonException exception)
        {
            throw new GitHubInvalidResponseException(
                message: "GitHub GraphQL response body could not be read as JSON",
                innerException: exception
            );
        }
    }

    // Kept relative so HttpClient resolves it against BaseAddress and it can only reach the configured host
    private static Uri ResolveGraphQlUri(Uri baseAddress)
    {
        return baseAddress.AbsolutePath.EndsWith(
            value: ENTERPRISE_REST_PATH_SUFFIX,
            comparisonType: StringComparison.Ordinal
        )
            ? EnterpriseGraphQlUri
            : GraphQlUri;
    }

    private static async ValueTask<ReadOnlyMemoryContent> BuildRequestContentAsync<TVariables>(
        string query,
        TVariables variables,
        JsonTypeInfo<TVariables> variablesTypeInfo,
        CancellationToken cancellationToken
    )
    {
        ArrayBufferWriter<byte> buffer = new();

        await using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString(propertyName: "query", value: query);
            writer.WritePropertyName("variables");
            JsonSerializer.Serialize(writer: writer, value: variables, jsonTypeInfo: variablesTypeInfo);
            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken);
        }

        ReadOnlyMemoryContent content = new(buffer.WrittenMemory);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType: "application/json", charSet: "utf-8");

        return content;
    }

    private static TData ReadData<TData>(GraphQlResponse<TData> graphQlResponse)
    {
        // GraphQL reports failures with HTTP 200 and an errors array, often alongside partial data
        if (HasErrors(graphQlResponse.Errors))
        {
            throw new GitHubGraphQlException("GitHub GraphQL request returned errors")
            {
                Errors = DescribeErrors(graphQlResponse.Errors),
            };
        }

        return graphQlResponse.Data ?? throw new GitHubGraphQlException("GitHub GraphQL response contained no data");
    }

    // Only an absent, null or empty errors array means success; any other shape could be hiding a failure, so
    // returning the (possibly partial) data would misreport it as a success
    private static bool HasErrors(JsonElement errors)
    {
        return errors.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => false,
            JsonValueKind.Array => errors.GetArrayLength() != 0,
            _ => throw new GitHubInvalidResponseException("GitHub GraphQL response errors value was not an array"),
        };
    }

    private static IReadOnlyList<string> DescribeErrors(JsonElement errors)
    {
        return [.. errors.EnumerateArray().Select(DescribeError)];
    }

    private static string DescribeError(JsonElement error)
    {
        return
            error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty(propertyName: "message", out JsonElement message)
            && message.ValueKind == JsonValueKind.String
            && message.GetString() is { } text
            ? text
            : UNKNOWN_ERROR;
    }
}
