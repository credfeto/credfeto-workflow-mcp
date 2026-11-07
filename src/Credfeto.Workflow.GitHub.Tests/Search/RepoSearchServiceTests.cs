using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Search;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Search;

public sealed class RepoSearchServiceTests : GitHubClientTestBase
{
    private const string OWNER = "credfeto";
    private const string NOT_FOUND = "the owner credfeto was not found, or the caller cannot search it";
    private const string FAILED = "could not search the repositories of credfeto";
    private const int LIMIT = 30;

    private static string Item(
        string fullName,
        string visibility = "public",
        string archived = "false",
        string defaultBranch = "\"main\""
    )
    {
        return "{\"full_name\":\""
            + fullName
            + "\",\"visibility\":\""
            + visibility
            + "\",\"archived\":"
            + archived
            + ",\"default_branch\":"
            + defaultBranch
            + "}";
    }

    private static string Page(params string[] items)
    {
        return PageOf(totalCount: items.Length.ToString(CultureInfo.InvariantCulture), items: items);
    }

    private static string PageOf(string totalCount, params string[] items)
    {
        return "{\"total_count\":" + totalCount + ",\"items\":[" + string.Join(separator: ',', value: items) + "]}";
    }

    private static Uri SearchUri(string query, int perPage)
    {
        return new(
            GitHubServices.ApiBaseUrl
                + "search/repositories?q="
                + Uri.EscapeDataString(query)
                + "&per_page="
                + perPage.ToString(CultureInfo.InvariantCulture)
        );
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private RepoSearchService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(rest);
    }

    private async Task<RepoSearchResult> SearchAsync(
        RecordingHttpMessageHandler upstream,
        string text = "crash",
        bool includeArchived = false,
        int limit = LIMIT,
        string owner = OWNER
    )
    {
        return await this.CreateService(upstream)
            .SearchAsync(
                owner: owner,
                text: text,
                includeArchived: includeArchived,
                limit: limit,
                cancellationToken: this.CancellationToken()
            );
    }

    private async Task<RepoSearchResult> SearchJsonAsync(string json, int limit = LIMIT)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        return await this.SearchAsync(upstream: upstream, limit: limit);
    }

    private static IReadOnlyList<string> Names(RepoSearchResult result)
    {
        return [.. result.Repositories.Select(entry => entry.Repository)];
    }

    [Theory]
    [InlineData(false, "in:name,description crash user:credfeto archived:false")]
    [InlineData(true, "in:name,description crash user:credfeto")]
    public async Task SendsOneSearchScopedToTheOwnerAsync(bool includeArchived, string query)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Page(Item("credfeto/a"))));

        RepoSearchResult result = await this.SearchAsync(upstream: upstream, includeArchived: includeArchived);

        Assert.Equal(expected: OWNER, actual: result.Owner);
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
        Assert.Equal(expected: SearchUri(query: query, perPage: LIMIT + 1), actual: request.RequestUri);
    }

    [Fact]
    public async Task QualifiersInTheTextCannotWidenTheSearchAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Page()));

        _ = await this.SearchAsync(upstream: upstream, text: "user:evil archived:true");

        string uri = Assert.IsType<Uri>(Assert.Single(upstream.Requests).RequestUri).AbsoluteUri;
        Assert.Contains(expectedSubstring: "user%3Acredfeto", actualString: uri, StringComparison.Ordinal);
        Assert.DoesNotContain(expectedSubstring: "user%3Aevil", actualString: uri, StringComparison.Ordinal);
        Assert.DoesNotContain(expectedSubstring: "archived%3Atrue", actualString: uri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "&per_page=2")]
    [InlineData(100, "&per_page=100")]
    public async Task LimitDecidesThePageSizeAsync(int limit, string perPage)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Page()));

        _ = await this.SearchAsync(upstream: upstream, limit: limit);

        Assert.EndsWith(
            expectedEndString: perPage,
            actualString: Assert.IsType<Uri>(Assert.Single(upstream.Requests).RequestUri).AbsoluteUri,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task RepositoriesAreSortedByNameAsync()
    {
        RepoSearchResult result = await this.SearchJsonAsync(
            Page(Item("credfeto/zeta"), Item("credfeto/Beta"), Item("credfeto/alpha"))
        );

        Assert.Equal(expected: ["credfeto/Beta", "credfeto/alpha", "credfeto/zeta"], actual: Names(result));
        Assert.False(condition: result.Truncated, userMessage: "Exactly the matches should not be truncated");
    }

    [Fact]
    public async Task MoreThanTheLimitIsTruncatedAsync()
    {
        RepoSearchResult result = await this.SearchJsonAsync(
            json: Page(Item("credfeto/d"), Item("credfeto/c"), Item("credfeto/b"), Item("credfeto/a")),
            limit: 3
        );

        Assert.Equal(expected: ["credfeto/b", "credfeto/c", "credfeto/d"], actual: Names(result));
        Assert.True(condition: result.Truncated, userMessage: "More than the limit should be truncated");
    }

    [Fact]
    public async Task TotalCountAboveTheLimitIsTruncatedAsync()
    {
        RepoSearchResult result = await this.SearchJsonAsync(
            json: PageOf(totalCount: "4", Item("credfeto/a")),
            limit: 3
        );

        Assert.True(condition: result.Truncated, userMessage: "A total count above the limit should be truncated");
    }

    [Fact]
    public async Task NoMatchIsEmptyAsync()
    {
        RepoSearchResult result = await this.SearchJsonAsync(Page());

        Assert.Equal(expected: OWNER, actual: result.Owner);
        Assert.Empty(result.Repositories);
        Assert.False(condition: result.Truncated, userMessage: "No match should not be truncated");
    }

    [Theory]
    [InlineData("public", "false", "\"main\"", "PUBLIC", "active", "main")]
    [InlineData("private", "true", "\"develop\"", "PRIVATE", "archived", "develop")]
    [InlineData("internal", "false", "null", "INTERNAL", "active", "-")]
    [InlineData("public", "false", "\"\"", "PUBLIC", "active", "-")]
    public async Task ProjectsEveryFieldAsync(
        string visibility,
        string archived,
        string defaultBranch,
        string expectedVisibility,
        string expectedStatus,
        string expectedDefaultBranch
    )
    {
        RepoSearchResult result = await this.SearchJsonAsync(
            Page(
                Item(
                    fullName: "credfeto/credfeto-workflow-mcp",
                    visibility: visibility,
                    archived: archived,
                    defaultBranch: defaultBranch
                )
            )
        );

        RepoSearchEntry entry = Assert.Single(result.Repositories);
        Assert.Equal(expected: "credfeto/credfeto-workflow-mcp", actual: entry.Repository);
        Assert.Equal(expected: expectedVisibility, actual: entry.Visibility);
        Assert.Equal(expected: expectedStatus, actual: entry.Status);
        Assert.Equal(expected: expectedDefaultBranch, actual: entry.DefaultBranch);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"items\":null}")]
    [InlineData("{\"items\":[null]}")]
    [InlineData("{\"items\":[{\"visibility\":\"public\",\"archived\":false}]}")]
    [InlineData("{\"items\":[{\"full_name\":\" \",\"visibility\":\"public\",\"archived\":false}]}")]
    [InlineData("{\"items\":[{\"full_name\":\"credfeto/a\",\"visibility\":\"public\"}]}")]
    [InlineData("{\"items\":[{\"full_name\":\"credfeto/a\",\"visibility\":\"secret\",\"archived\":false}]}")]
    [InlineData("{\"items\":[{\"full_name\":\"credfeto/a\",\"archived\":false}]}")]
    public async Task InvalidResponseFailsTheSearchAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        RepoSearchFailedException exception = await Assert.ThrowsAsync<RepoSearchFailedException>(() =>
            this.SearchAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task UnprocessableIsReportedAsMissingOwnerAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(HttpStatusCode.UnprocessableEntity));

        OwnerNotFoundException exception = await Assert.ThrowsAsync<OwnerNotFoundException>(() =>
            this.SearchAsync(upstream)
        );

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task UpstreamErrorFailsTheSearchAsync(HttpStatusCode status)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(status));

        RepoSearchFailedException exception = await Assert.ThrowsAsync<RepoSearchFailedException>(() =>
            this.SearchAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-credfeto")]
    [InlineData("credfeto/name")]
    public async Task InvalidOwnerIsRefusedWithoutRequestAsync(string owner)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.SearchAsync(upstream: upstream, owner: owner)
        );

        Assert.Equal(expected: "owner", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task InvalidLimitIsRefusedWithoutRequestAsync(int limit)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.SearchAsync(upstream: upstream, limit: limit)
        );

        Assert.Equal(expected: "limit", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("---")]
    [InlineData("crash OR boom")]
    public async Task InvalidTextIsRefusedWithoutRequestAsync(string text)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.SearchAsync(upstream: upstream, text: text)
        );

        Assert.Equal(expected: "text", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task RefusedTextIsRefusedWithoutRequestAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        IssueSearchRefusedException exception = await Assert.ThrowsAsync<IssueSearchRefusedException>(() =>
            this.SearchAsync(upstream: upstream, text: "see /home/user/notes")
        );

        Assert.Equal(expected: "host path", actual: exception.Rule);
        Assert.Empty(upstream.Requests);
    }
}
