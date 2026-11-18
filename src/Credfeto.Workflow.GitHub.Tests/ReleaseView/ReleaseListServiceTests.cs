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
using Credfeto.Workflow.GitHub.ReleaseView;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.ReleaseView;

public sealed class ReleaseListServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string FAILED = "could not list the releases of credfeto/credfeto-workflow-mcp";
    private const string TOO_MANY = "credfeto/credfeto-workflow-mcp has more than 1000 releases, so none is printed";
    private const string NOT_FOUND =
        "the repository credfeto/credfeto-workflow-mcp was not found, or the caller cannot see it";
    private const string PUBLISHED_AT = "2026-11-17T09:39:14Z";

    private static string Node(
        string tag = "\"v1.0.0\"",
        string name = "\"Release 1.0.0\"",
        string draft = "false",
        string prerelease = "false",
        string latest = "false",
        string publishedAt = "\"" + PUBLISHED_AT + "\""
    )
    {
        return "{\"tagName\":"
            + tag
            + ",\"name\":"
            + name
            + ",\"isDraft\":"
            + draft
            + ",\"isPrerelease\":"
            + prerelease
            + ",\"isLatest\":"
            + latest
            + ",\"publishedAt\":"
            + publishedAt
            + "}";
    }

    private static string Published(string tag, string publishedAt)
    {
        return Node(tag: "\"" + tag + "\"", publishedAt: "\"" + publishedAt + "\"");
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

    private static string Data(string releases)
    {
        return "{\"data\":{\"repository\":{\"releases\":" + releases + "}}}";
    }

    private static HttpResponseMessage Page(params string[] nodes)
    {
        return Responses.Json(
            Data(Connection(totalCount: nodes.Length, nodes: string.Join(separator: ',', value: nodes)))
        );
    }

    private static string Nodes(int count, string? node = null)
    {
        return string.Join(separator: ',', values: Enumerable.Repeat(element: node ?? Node(), count: count));
    }

    private static string FullPage(int totalCount, string? endCursor)
    {
        return Data(Connection(totalCount: totalCount, nodes: Nodes(100), hasNextPage: true, endCursor: endCursor));
    }

    private static string Cursor(int index)
    {
        return "cursor-" + index.ToString(CultureInfo.InvariantCulture);
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

    private ReleaseService CreateService(RecordingHttpMessageHandler upstream)
    {
        IHttpClientFactory httpClientFactory = MockHttpClientFactoryCreateClient(upstream);
        GitHubRestClient rest = new(
            httpClientFactory: httpClientFactory,
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );
        GitHubGraphQlClient graphQl = new(
            httpClientFactory: httpClientFactory,
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        return new(restClient: rest, graphQlClient: graphQl);
    }

    private async Task<ReleaseListResponse> ListAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY
    )
    {
        return await this.CreateService(upstream)
            .ListAsync(repository: repository, cancellationToken: this.CancellationToken());
    }

    private async Task<ReleaseListFailedException> ListFailedAsync(RecordingHttpMessageHandler upstream)
    {
        ReleaseListFailedException exception = await Assert.ThrowsAsync<ReleaseListFailedException>(() =>
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

    private static IReadOnlyList<string> Tags(ReleaseListResponse response)
    {
        return [.. response.Releases.Select(release => release.Tag)];
    }

    [Fact]
    public async Task ProjectsEveryFieldAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(
                Node(
                    tag: "\"v2.0.0\"",
                    name: "\"Release 2.0.0\"",
                    latest: "true",
                    publishedAt: "\"2026-11-17T10:00:00Z\""
                ),
                Node(
                    tag: "\"v2.1.0-beta\"",
                    name: "\"Beta\"",
                    prerelease: "true",
                    publishedAt: "\"2026-11-16T10:00:00Z\""
                )
            )
        );

        ReleaseListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        Assert.Equal(
            expected:
            [
                new ReleaseSummary(
                    Tag: "v2.0.0",
                    PublishedAt: "2026-11-17T10:00:00Z",
                    IsLatest: true,
                    IsPrerelease: false,
                    Name: "Release 2.0.0"
                ),
                new ReleaseSummary(
                    Tag: "v2.1.0-beta",
                    PublishedAt: "2026-11-16T10:00:00Z",
                    IsLatest: false,
                    IsPrerelease: true,
                    Name: "Beta"
                ),
            ],
            actual: response.Releases
        );
    }

    [Fact]
    public async Task TheQueryAsksForTheRepositoryWithoutReleaseNotesAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Page(Node()));

        _ = await this.ListAsync(upstream);

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Post, actual: request.Method);
        JsonElement variables = Variables(request);
        Assert.Equal(expected: "credfeto", actual: variables.GetProperty("o").GetString());
        Assert.Equal(expected: "credfeto-workflow-mcp", actual: variables.GetProperty("r").GetString());
        Assert.Equal(expected: JsonValueKind.Null, actual: variables.GetProperty("c").ValueKind);

        string query = Query(request);
        Assert.Contains(
            expectedSubstring: "repository(owner:$o,name:$r)",
            actualString: query,
            comparisonType: StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            expectedSubstring: "description",
            actualString: query,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task NoReleasesIsEmptyAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Page());

        ReleaseListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        Assert.Empty(response.Releases);
    }

    [Fact]
    public async Task ReleasesAreNewestFirstAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(
                Published(tag: "v1.0.0", publishedAt: "2026-01-01T00:00:00Z"),
                Published(tag: "v3.0.0", publishedAt: "2026-03-01T00:00:00Z"),
                Published(tag: "v2.0.0", publishedAt: "2026-02-01T00:00:00Z")
            )
        );

        ReleaseListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: ["v3.0.0", "v2.0.0", "v1.0.0"], actual: Tags(response));
    }

    [Fact]
    public async Task ReleasesPublishedTogetherAreOrderedByTagAsOrdinalTextAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(
                Published(tag: "v1.0.0", publishedAt: PUBLISHED_AT),
                Published(tag: "v10", publishedAt: PUBLISHED_AT),
                Published(tag: "v1.0.1", publishedAt: PUBLISHED_AT),
                Published(tag: "v9", publishedAt: PUBLISHED_AT)
            )
        );

        ReleaseListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: ["v9", "v10", "v1.0.1", "v1.0.0"], actual: Tags(response));
    }

    [Fact]
    public async Task TheLatestReleaseIsNotAlwaysFirstAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(
                Node(tag: "\"v2.0.0\"", latest: "true", publishedAt: "\"2026-01-01T00:00:00Z\""),
                Node(tag: "\"v1.9.1\"", publishedAt: "\"2026-02-01T00:00:00Z\"")
            )
        );

        ReleaseListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: ["v1.9.1", "v2.0.0"], actual: Tags(response));
        Assert.False(response.Releases[0].IsLatest, userMessage: "the backport is not the latest release");
        Assert.True(response.Releases[1].IsLatest, userMessage: "the latest release is second");
    }

    [Fact]
    public async Task DraftsAreDroppedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(
                Node(tag: "\"v1.0.0\""),
                Node(tag: "\"v2.0.0-draft\"", draft: "true", publishedAt: "null"),
                Node(tag: "\"v0.9.0\"", publishedAt: "\"2026-01-01T00:00:00Z\"")
            )
        );

        ReleaseListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: ["v1.0.0", "v0.9.0"], actual: Tags(response));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\" \"")]
    public async Task AMissingNameIsADashAsync(string name)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Page(Node(name: name)));

        ReleaseListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: "-", actual: Assert.Single(response.Releases).Name);
    }

    [Fact]
    public async Task ControlCharactersBecomeSpacesAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(Node(tag: "\"v1\\n0\"", name: "\"first\\r\\nsecond\\u001b[31m\""))
        );

        ReleaseListResponse response = await this.ListAsync(upstream);

        ReleaseSummary release = Assert.Single(response.Releases);
        Assert.Equal(expected: "v1 0", actual: release.Tag);
        Assert.Equal(expected: "first  second [31m", actual: release.Name);
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
                            totalCount: 101,
                            nodes: Published(tag: "v1.0.0", publishedAt: "2026-01-01T00:00:00Z"),
                            hasNextPage: true,
                            endCursor: "cursor-1"
                        )
                    )
                ),
                1 => Responses.Json(
                    Data(
                        Connection(
                            totalCount: 101,
                            nodes: Published(tag: "v2.0.0", publishedAt: "2026-02-01T00:00:00Z")
                        )
                    )
                ),
                _ => Unexpected(index),
            }
        );

        ReleaseListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: ["v2.0.0", "v1.0.0"], actual: Tags(response));
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        Assert.Equal(expected: JsonValueKind.Null, actual: Variables(upstream.Requests[0]).GetProperty("c").ValueKind);
        JsonElement second = Variables(upstream.Requests[1]);
        Assert.Equal(expected: "cursor-1", actual: second.GetProperty("c").GetString());
        Assert.Equal(expected: "credfeto", actual: second.GetProperty("o").GetString());
        Assert.Equal(expected: "credfeto-workflow-mcp", actual: second.GetProperty("r").GetString());
    }

    [Fact]
    public async Task ExactlyTheCapIsListedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index < 9
                ? Responses.Json(FullPage(totalCount: 1000, endCursor: Cursor(index)))
                : Responses.Json(Data(Connection(totalCount: 1000, nodes: Nodes(100))))
        );

        ReleaseListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: 1000, actual: response.Releases.Count);
        Assert.Equal(expected: 10, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task MoreThanTheCapIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            Responses.Json(FullPage(totalCount: 1100, endCursor: Cursor(index)))
        );

        ReleaseListRefusedException exception = await Assert.ThrowsAsync<ReleaseListRefusedException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: TOO_MANY, actual: exception.Message);
        Assert.Equal(expected: 11, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task DraftsDoNotCountTowardsTheCapAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index < 10
                ? Responses.Json(FullPage(totalCount: 1050, endCursor: Cursor(index)))
                : Responses.Json(Data(Connection(totalCount: 1050, nodes: Nodes(count: 50, node: Node(draft: "true")))))
        );

        ReleaseListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: 1000, actual: response.Releases.Count);
        Assert.Equal(expected: 11, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task ReadingStopsAfterTheCountedPagesAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            Responses.Json(FullPage(totalCount: 150, endCursor: Cursor(index)))
        );

        ReleaseListFailedException exception = await this.ListFailedAsync(upstream);

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
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

        ReleaseListFailedException exception = await this.ListFailedAsync(upstream);

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task RepeatedCursorIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json(FullPage(totalCount: 300, endCursor: "same-cursor"))
        );

        ReleaseListFailedException exception = await this.ListFailedAsync(upstream);

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task AMissingRepositoryIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json(
                """{"data":{"repository":null},"errors":[{"type":"NOT_FOUND","message":"Could not resolve to a Repository with the name 'credfeto/credfeto-workflow-mcp'."}]}"""
            )
        );

        RepositoryNotFoundException exception = await Assert.ThrowsAsync<RepositoryNotFoundException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
    }

    [Fact]
    public async Task ANullRepositoryIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"data":{"repository":null}}""")
        );

        RepositoryNotFoundException exception = await Assert.ThrowsAsync<RepositoryNotFoundException>(() =>
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

        ReleaseListFailedException exception = await this.ListFailedAsync(upstream);

        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
    }

    [Fact]
    public async Task UnreadableBodyIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Html("<html></html>"));

        ReleaseListFailedException exception = await this.ListFailedAsync(upstream);

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task HttpFailureIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        ReleaseListFailedException exception = await this.ListFailedAsync(upstream);

        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task NullDataIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json("""{"data":null}"""));

        _ = await this.ListFailedAsync(upstream);
    }

    [Theory]
    [InlineData("\"tagName\":\"v1.0.0\"", "\"tagName\":null")]
    [InlineData("\"tagName\":\"v1.0.0\"", "\"tagName\":\" \"")]
    [InlineData("\"publishedAt\":\"" + PUBLISHED_AT + "\"", "\"publishedAt\":null")]
    [InlineData("\"isDraft\":false", "\"isDraft\":null")]
    [InlineData("\"isLatest\":false", "\"isLatest\":null")]
    [InlineData("\"isPrerelease\":false", "\"isPrerelease\":null")]
    [InlineData("\"nodes\":[" + "{", "\"nodes\":[null,{")]
    [InlineData("\"nodes\":[", "\"nodes\":null,\"unused\":[")]
    [InlineData("\"totalCount\":1", "\"totalCount\":null")]
    [InlineData("\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null}", "\"pageInfo\":null")]
    [InlineData("\"hasNextPage\":false", "\"hasNextPage\":null")]
    [InlineData("{\"releases\":{", "{\"releases\":null,\"unused\":{")]
    public async Task InvalidResponseIsFailedAsync(string oldValue, string newValue)
    {
        string json = Mutate(
            source: Data(Connection(totalCount: 1, nodes: Node())),
            oldValue: oldValue,
            newValue: newValue
        );

        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        ReleaseListFailedException exception = await this.ListFailedAsync(upstream);

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Theory]
    [InlineData("")]
    [InlineData("credfeto")]
    [InlineData("credfeto/")]
    [InlineData("/credfeto-workflow-mcp")]
    [InlineData("credfeto/credfeto-workflow-mcp/extra")]
    [InlineData("credfeto/bad name")]
    public async Task InvalidRepositoryIsRejectedWithoutRequestAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ListAsync(upstream: upstream, repository: repository)
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }
}
