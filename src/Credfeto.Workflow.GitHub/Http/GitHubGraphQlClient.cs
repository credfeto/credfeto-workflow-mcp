using System;
using System.Buffers;
using System.Collections.Generic;
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
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.Models;
using Microsoft.Extensions.Logging;

namespace Credfeto.Workflow.GitHub.Http;

public sealed class GitHubGraphQlClient : IGitHubGraphQlClient
{
    private const string UNKNOWN_ERROR = "unknown error";

    private const string UNREADABLE_RESPONSE = "GitHub GraphQL response body could not be read as JSON";

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
        JsonTypeInfo<TData> dataTypeInfo,
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

        GraphQlResponse graphQlResponse = await GitHubJsonBody.ReadAsync(
            content: response.Content,
            typeInfo: GitHubJsonContext.Default.GraphQlResponse,
            nullMessage: "GitHub GraphQL response body was null",
            unreadableMessage: UNREADABLE_RESPONSE,
            cancellationToken: cancellationToken
        );

        return ReadData(graphQlResponse: graphQlResponse, dataTypeInfo: dataTypeInfo);
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

    private static TData ReadData<TData>(GraphQlResponse graphQlResponse, JsonTypeInfo<TData> dataTypeInfo)
    {
        // GraphQL reports failures with HTTP 200 and an errors array, often alongside partial data
        if (HasErrors(graphQlResponse.Errors))
        {
            throw new GitHubGraphQlException("GitHub GraphQL request returned errors")
            {
                Errors = DescribeErrors(graphQlResponse.Errors),
            };
        }

        // Checked on the element rather than the result because a value-type TData would turn a missing or null
        // data into default(TData) and report it as a success
        if (graphQlResponse.Data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new GitHubGraphQlException("GitHub GraphQL response contained no data");
        }

        try
        {
            // Non-null: a null data element has already been rejected above
            return JsonSerializer.Deserialize(element: graphQlResponse.Data, jsonTypeInfo: dataTypeInfo)!;
        }
        catch (JsonException exception)
        {
            // The fixed message is what callers see; the JsonException, whose message can quote the upstream body,
            // is kept only as the inner exception for server-side diagnostics
            throw new GitHubInvalidResponseException(message: UNREADABLE_RESPONSE, innerException: exception);
        }
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
