using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Repositories;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Repositories;

public sealed class RepositoryListServiceTests : GitHubClientTestBase
{
    private const string OWNER = "credfeto";
    private const string FAILED = "could not list the repositories of credfeto";
    private const string TOO_MANY = "credfeto has more than 1000 repositories to list";
    private const string NOT_FOUND = "the owner credfeto was not found, or the caller cannot see it";

    private static string Node(
        string name = "\"credfeto/alpha\"",
        string visibility = "\"PUBLIC\"",
        string archived = "false",
        string defaultBranch = "{\"name\":\"main\"}"
    )
    {
        return "{\"nameWithOwner\":"
            + name
            + ",\"visibility\":"
            + visibility
            + ",\"isArchived\":"
            + archived
            + ",\"defaultBranchRef\":"
            + defaultBranch
            + "}";
    }

    private static string NamedNode(string name)
    {
        return Node(name: "\"" + name + "\"");
    }

    private static string Connection(int totalCount, string nodes, bool hasNextPage = false, string? endCursor = null)
    {
        string cursor = endCursor is null ? "null" : "\"" + endCursor + "\"";

        return "{\"totalCount\":"
            + totalCount.ToString(CultureInfo.InvariantCulture)
            + ",\"pageInfo\":{\"hasNextPage\":"
            + (hasNextPage ? "true" : "false")
            + ",\"endCursor\":"
            + cursor
            + "},\"nodes\":["
            + nodes
            + "]}";
    }

    private static string Data(string repositories)
    {
        return "{\"data\":{\"repositoryOwner\":{\"repositories\":" + repositories + "}}}";
    }

    private static HttpResponseMessage Page(params string[] nodes)
    {
        return Responses.Json(
            Data(Connection(totalCount: nodes.Length, nodes: string.Join(separator: ',', value: nodes)))
        );
    }

    private static string FullPage(int totalCount, string? endCursor)
    {
        return Data(
            Connection(
                totalCount: totalCount,
                nodes: string.Join(separator: ',', values: Enumerable.Repeat(element: Node(), count: 100)),
                hasNextPage: true,
                endCursor: endCursor
            )
        );
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static string Mutate(string source, string oldValue, string newValue)
    {
        // Guards against a typo in the test silently leaving the payload unchanged
        Assert.Contains(expectedSubstring: oldValue, actualString: source, comparisonType: StringComparison.Ordinal);

        return source.Replace(oldValue: oldValue, newValue: newValue, comparisonType: StringComparison.Ordinal);
    }

    private RepositoryListService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubGraphQlClient client = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        return new(client);
    }

    private async Task<RepositoryListResponse> ListAsync(
        RecordingHttpMessageHandler upstream,
        string owner = OWNER,
        bool includeArchived = false
    )
    {
        return await this.CreateService(upstream)
            .ListAsync(owner: owner, includeArchived: includeArchived, cancellationToken: this.CancellationToken());
    }

    private async Task<RepositoryListResponse> ListJsonAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        return await this.ListAsync(upstream);
    }

    private async Task<RepositoryListFailedException> ListFailedAsync(RecordingHttpMessageHandler upstream)
    {
        RepositoryListFailedException exception = await Assert.ThrowsAsync<RepositoryListFailedException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);

        return exception;
    }

    private static JsonElement Variables(RecordedRequest request)
    {
        using JsonDocument body = JsonDocument.Parse(request.Body ?? string.Empty);

        return body.RootElement.GetProperty("variables").Clone();
    }

    private static string Query(RecordedRequest request)
    {
        using JsonDocument body = JsonDocument.Parse(request.Body ?? string.Empty);

        return body.RootElement.GetProperty("query").GetString() ?? string.Empty;
    }

    private static IReadOnlyList<string> Names(RepositoryListResponse response)
    {
        return [.. response.Repositories.Select(entry => entry.Repository)];
    }

    [Fact]
    public async Task ProjectsEveryFieldAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(
                Node(name: "\"credfeto/alpha\"", visibility: "\"PUBLIC\"", archived: "false"),
                Node(name: "\"credfeto/beta\"", visibility: "\"PRIVATE\"", archived: "true", defaultBranch: "null"),
                Node(name: "\"credfeto/gamma\"", visibility: "\"INTERNAL\"", defaultBranch: "{\"name\":\"develop\"}")
            )
        );

        RepositoryListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: OWNER, actual: response.Owner);
        Assert.Equal(
            expected:
            [
                new RepositoryEntry(
                    Repository: "credfeto/alpha",
                    Visibility: "PUBLIC",
                    Archived: false,
                    DefaultBranch: "main"
                ),
                new RepositoryEntry(
                    Repository: "credfeto/beta",
                    Visibility: "PRIVATE",
                    Archived: true,
                    DefaultBranch: "-"
                ),
                new RepositoryEntry(
                    Repository: "credfeto/gamma",
                    Visibility: "INTERNAL",
                    Archived: false,
                    DefaultBranch: "develop"
                ),
            ],
            actual: response.Repositories
        );
    }

    [Fact]
    public async Task ArchivedRepositoriesAreExcludedByDefaultAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Page(Node()));

        _ = await this.ListAsync(upstream);

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Post, actual: request.Method);
        JsonElement variables = Variables(request);
        Assert.Equal(expected: OWNER, actual: variables.GetProperty("o").GetString());
        Assert.False(variables.GetProperty("a").GetBoolean(), userMessage: "archived should be filtered out");
        Assert.Contains(
            expectedSubstring: "repositoryOwner(login:$o)",
            actualString: Query(request),
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ArchivedRepositoriesAreIncludedWhenAskedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Page(Node(archived: "true")));

        RepositoryListResponse response = await this.ListAsync(upstream: upstream, includeArchived: true);

        Assert.True(Assert.Single(response.Repositories).Archived, userMessage: "archived repository should be listed");
        JsonElement variables = Variables(Assert.Single(upstream.Requests));
        Assert.False(variables.TryGetProperty(propertyName: "a", value: out _), userMessage: "no archived filter");
    }

    [Fact]
    public async Task NoRepositoriesIsEmptyAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Page());

        RepositoryListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: OWNER, actual: response.Owner);
        Assert.Empty(response.Repositories);
    }

    [Fact]
    public async Task RepositoriesAreSortedByNameIgnoringCaseAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(
                NamedNode("credfeto/zeta"),
                NamedNode("credfeto/Beta"),
                NamedNode("credfeto/alpha"),
                NamedNode("credfeto/beta")
            )
        );

        RepositoryListResponse response = await this.ListAsync(upstream);

        Assert.Equal(
            expected: ["credfeto/alpha", "credfeto/Beta", "credfeto/beta", "credfeto/zeta"],
            actual: Names(response)
        );
    }

    [Fact]
    public async Task EveryPageIsReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index switch
            {
                0 => Responses.Json(
                    Data(
                        Connection(
                            totalCount: 2,
                            nodes: NamedNode("credfeto/second"),
                            hasNextPage: true,
                            endCursor: "cursor-1"
                        )
                    )
                ),
                1 => Responses.Json(Data(Connection(totalCount: 2, nodes: NamedNode("credfeto/first")))),
                _ => Unexpected(index),
            }
        );

        RepositoryListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: ["credfeto/first", "credfeto/second"], actual: Names(response));
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        Assert.Equal(expected: JsonValueKind.Null, actual: Variables(upstream.Requests[0]).GetProperty("c").ValueKind);
        Assert.Equal(expected: "cursor-1", actual: Variables(upstream.Requests[1]).GetProperty("c").GetString());
    }

    [Fact]
    public async Task MoreThanTheCapIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json(FullPage(totalCount: 1001, endCursor: "cursor-1"))
        );

        RepositoryListRefusedException exception = await Assert.ThrowsAsync<RepositoryListRefusedException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: TOO_MANY, actual: exception.Message);
        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task ExactlyTheCapIsListedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index < 9
                ? Responses.Json(
                    FullPage(totalCount: 1000, endCursor: "cursor-" + index.ToString(CultureInfo.InvariantCulture))
                )
                : Responses.Json(
                    Data(
                        Connection(
                            totalCount: 1000,
                            nodes: string.Join(separator: ',', values: Enumerable.Repeat(element: Node(), count: 100))
                        )
                    )
                )
        );

        RepositoryListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: 1000, actual: response.Repositories.Count);
        Assert.Equal(expected: 10, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task ReadingStopsAfterTenPagesAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            Responses.Json(
                FullPage(totalCount: 1000, endCursor: "cursor-" + index.ToString(CultureInfo.InvariantCulture))
            )
        );

        RepositoryListFailedException exception = await this.ListFailedAsync(upstream);

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: 10, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad cursor")]
    public async Task InvalidCursorIsFailedAsync(string? cursor)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Json(FullPage(totalCount: 200, endCursor: cursor))
        );

        RepositoryListFailedException exception = await this.ListFailedAsync(upstream);

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task RepeatedCursorIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json(FullPage(totalCount: 300, endCursor: "same-cursor"))
        );

        RepositoryListFailedException exception = await this.ListFailedAsync(upstream);

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task MissingOwnerIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"data":{"repositoryOwner":null}}""")
        );

        OwnerNotFoundException exception = await Assert.ThrowsAsync<OwnerNotFoundException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
    }

    [Fact]
    public async Task GraphQlErrorIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"errors":[{"message":"Something went wrong"}],"data":null}""")
        );

        RepositoryListFailedException exception = await this.ListFailedAsync(upstream);

        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
    }

    [Fact]
    public async Task UnreadableBodyIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Html("<html></html>"));

        RepositoryListFailedException exception = await this.ListFailedAsync(upstream);

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task HttpFailureIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        RepositoryListFailedException exception = await this.ListFailedAsync(upstream);

        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData("\"visibility\":\"PUBLIC\"", "\"visibility\":\"SECRET\"")]
    [InlineData("\"visibility\":\"PUBLIC\"", "\"visibility\":\"public\"")]
    [InlineData("\"visibility\":\"PUBLIC\"", "\"visibility\":null")]
    [InlineData("\"nameWithOwner\":\"credfeto/alpha\"", "\"nameWithOwner\":null")]
    [InlineData("\"nameWithOwner\":\"credfeto/alpha\"", "\"nameWithOwner\":\" \"")]
    [InlineData("\"isArchived\":false", "\"isArchived\":null")]
    [InlineData("\"nodes\":[" + "{", "\"nodes\":[null,{")]
    [InlineData("\"nodes\":[", "\"nodes\":null,\"unused\":[")]
    [InlineData("\"totalCount\":1", "\"totalCount\":null")]
    [InlineData("\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null}", "\"pageInfo\":null")]
    [InlineData("\"hasNextPage\":false", "\"hasNextPage\":null")]
    [InlineData("{\"repositories\":{", "{\"repositories\":null,\"unused\":{")]
    public async Task InvalidResponseIsFailedAsync(string oldValue, string newValue)
    {
        string json = Mutate(
            source: Data(Connection(totalCount: 1, nodes: Node())),
            oldValue: oldValue,
            newValue: newValue
        );

        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        RepositoryListFailedException exception = await this.ListFailedAsync(upstream);

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task NullDataIsFailedAsync()
    {
        RepositoryListFailedException exception = await Assert.ThrowsAsync<RepositoryListFailedException>(() =>
            this.ListJsonAsync("""{"data":null}""")
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("credfeto/credfeto-workflow-mcp")]
    [InlineData("bad owner")]
    [InlineData("-credfeto")]
    public async Task InvalidOwnerIsRejectedWithoutRequestAsync(string owner)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ListAsync(upstream: upstream, owner: owner)
        );

        Assert.Contains(
            expectedSubstring: "owner",
            actualString: exception.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Empty(upstream.Requests);
    }
}
