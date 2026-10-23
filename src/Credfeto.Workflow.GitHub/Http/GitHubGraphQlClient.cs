using System;
using System.Buffers;
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
using Microsoft.Extensions.Logging;

namespace Credfeto.Workflow.GitHub.Http;

public sealed class GitHubGraphQlClient : IGitHubGraphQlClient
{
    private const string UNKNOWN_ERROR = "unknown error";

    private static readonly Uri GraphQlUri = new(uriString: "graphql", uriKind: UriKind.Relative);

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

        using HttpRequestMessage request = new(method: HttpMethod.Post, requestUri: GraphQlUri);
        request.Content = await BuildRequestContentAsync(
            query: query,
            variables: variables,
            variablesTypeInfo: variablesTypeInfo,
            cancellationToken: cancellationToken
        );

        using HttpResponseMessage response = await httpClient.SendAsync(
            request: request,
            cancellationToken: cancellationToken
        );

        if (!response.IsSuccessStatusCode)
        {
            this._logger.LogRequestFailed((int)response.StatusCode);
            throw GitHubHttpFailure.FromResponse(response);
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(
            utf8Json: stream,
            cancellationToken: cancellationToken
        );

        return ReadData(root: document.RootElement, dataTypeInfo: dataTypeInfo);
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

    private static TData ReadData<TData>(JsonElement root, JsonTypeInfo<TData> dataTypeInfo)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new GitHubInvalidResponseException("GitHub GraphQL response was not a JSON object");
        }

        // GraphQL reports failures with HTTP 200 and an errors array, often alongside partial data
        if (root.TryGetProperty(propertyName: "errors", out JsonElement errors) && HasErrors(errors))
        {
            throw new GitHubGraphQlException(DescribeErrors(errors));
        }

        if (!root.TryGetProperty(propertyName: "data", out JsonElement data))
        {
            throw new GitHubGraphQlException("GitHub GraphQL response contained no data");
        }

        return JsonSerializer.Deserialize(element: data, jsonTypeInfo: dataTypeInfo)
            ?? throw new GitHubGraphQlException("GitHub GraphQL response contained null data");
    }

    private static bool HasErrors(JsonElement errors)
    {
        return errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() != 0;
    }

    private static string DescribeErrors(JsonElement errors)
    {
        return "GitHub GraphQL request returned errors: "
            + string.Join(separator: "; ", values: errors.EnumerateArray().Select(DescribeError));
    }

    private static string? DescribeError(JsonElement error)
    {
        return
            error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty(propertyName: "message", out JsonElement message)
            && message.ValueKind == JsonValueKind.String
            ? message.GetString()
            : UNKNOWN_ERROR;
    }
}
