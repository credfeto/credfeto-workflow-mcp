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
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Search;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Search;

public sealed class PrSearchServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string NOT_FOUND =
        "the repository credfeto/credfeto-workflow-mcp was not found, or the caller cannot see it";
    private const string FAILED = "could not search the pull requests of credfeto/credfeto-workflow-mcp";
    private const int LIMIT = 30;
    private const string NOT_MERGED = "{\"merged_at\":null}";

    private static string Item(
        int number,
        string state = "open",
        string title = "\"A change\"",
        string pullRequest = NOT_MERGED
    )
    {
        return "{\"number\":"
            + number.ToString(CultureInfo.InvariantCulture)
            + ",\"state\":\""
            + state
            + "\",\"title\":"
            + title
            + ",\"pull_request\":"
            + pullRequest
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
                + "search/issues?q="
                + Uri.EscapeDataString(query)
                + "&per_page="
                + perPage.ToString(CultureInfo.InvariantCulture)
        );
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private PrSearchService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(rest);
    }

    private async Task<PrSearchResult> SearchAsync(
        RecordingHttpMessageHandler upstream,
        string text = "crash",
        PrSearchState state = PrSearchState.OPEN,
        int limit = LIMIT,
        string repository = REPOSITORY
    )
    {
        return await this.CreateService(upstream)
            .SearchAsync(
                repository: repository,
                text: text,
                state: state,
                limit: limit,
                cancellationToken: this.CancellationToken()
            );
    }

    private async Task<PrSearchResult> SearchJsonAsync(string json, int limit = LIMIT)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        return await this.SearchAsync(upstream: upstream, limit: limit);
    }

    private static IReadOnlyList<int> Numbers(PrSearchResult result)
    {
        return [.. result.PullRequests.Select(entry => entry.Number)];
    }

    [Theory]
    [InlineData(PrSearchState.OPEN, "in:title,body crash repo:credfeto/credfeto-workflow-mcp type:pr state:open")]
    [InlineData(PrSearchState.CLOSED, "in:title,body crash repo:credfeto/credfeto-workflow-mcp type:pr state:closed")]
    [InlineData(PrSearchState.MERGED, "in:title,body crash repo:credfeto/credfeto-workflow-mcp type:pr is:merged")]
    [InlineData(PrSearchState.ALL, "in:title,body crash repo:credfeto/credfeto-workflow-mcp type:pr")]
    public async Task SendsOneSearchScopedToTheRepositoryAsync(PrSearchState state, string query)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Page(Item(1))));

        PrSearchResult result = await this.SearchAsync(upstream: upstream, state: state);

        Assert.Equal(expected: REPOSITORY, actual: result.Repository);
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
        Assert.Equal(expected: SearchUri(query: query, perPage: LIMIT + 1), actual: request.RequestUri);
    }

    [Fact]
    public async Task QualifiersInTheTextCannotWidenTheSearchAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Page()));

        _ = await this.SearchAsync(upstream: upstream, text: "repo:evil/x type:issue");

        RecordedRequest request = Assert.Single(upstream.Requests);
        string uri = Assert.IsType<Uri>(request.RequestUri).AbsoluteUri;
        Assert.Contains(
            expectedSubstring: "repo%3Acredfeto%2Fcredfeto-workflow-mcp",
            actualString: uri,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(expectedSubstring: "repo%3Aevil", actualString: uri, StringComparison.Ordinal);
        Assert.DoesNotContain(expectedSubstring: "type%3Aissue", actualString: uri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "&per_page=2")]
    [InlineData(100, "&per_page=100")]
    public async Task LimitDecidesThePageSizeAsync(int limit, string perPage)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Page()));

        _ = await this.SearchAsync(upstream: upstream, limit: limit);

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.EndsWith(
            expectedEndString: perPage,
            actualString: Assert.IsType<Uri>(request.RequestUri).AbsoluteUri,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task MoreThanTheLimitIsTruncatedInOrderAsync()
    {
        PrSearchResult result = await this.SearchJsonAsync(json: Page(Item(9), Item(3), Item(5), Item(1)), limit: 3);

        Assert.Equal(expected: [9, 3, 5], actual: Numbers(result));
        Assert.True(condition: result.Truncated, userMessage: "More than the limit should be truncated");
    }

    [Fact]
    public async Task TotalCountAboveTheLimitIsTruncatedAsync()
    {
        PrSearchResult result = await this.SearchJsonAsync(json: PageOf(totalCount: "4", Item(9), Item(3)), limit: 3);

        Assert.Equal(expected: [9, 3], actual: Numbers(result));
        Assert.True(condition: result.Truncated, userMessage: "A total count above the limit should be truncated");
    }

    [Fact]
    public async Task ExactlyTheLimitIsNotTruncatedAsync()
    {
        PrSearchResult result = await this.SearchJsonAsync(json: Page(Item(9), Item(3), Item(5)), limit: 3);

        Assert.Equal(expected: [9, 3, 5], actual: Numbers(result));
        Assert.False(condition: result.Truncated, userMessage: "Exactly the limit should not be truncated");
    }

    [Fact]
    public async Task NoMatchIsEmptyAsync()
    {
        PrSearchResult result = await this.SearchJsonAsync(Page());

        Assert.Equal(expected: REPOSITORY, actual: result.Repository);
        Assert.Empty(result.PullRequests);
        Assert.False(condition: result.Truncated, userMessage: "No match should not be truncated");
    }

    [Fact]
    public async Task ProjectsEveryFieldAsync()
    {
        PrSearchResult result = await this.SearchJsonAsync(Page(Item(number: 7, title: "\"Fix the crash\"")));

        PrSearchEntry entry = Assert.Single(result.PullRequests);
        Assert.Equal(expected: 7, actual: entry.Number);
        Assert.Equal(expected: "OPEN", actual: entry.State);
        Assert.Equal(expected: "-", actual: entry.HeadBranch);
        Assert.Equal(expected: "Fix the crash", actual: entry.Title);
    }

    [Theory]
    [InlineData("closed", NOT_MERGED, "CLOSED")]
    [InlineData("closed", "{}", "CLOSED")]
    [InlineData("closed", "{\"merged_at\":\"2026-01-02T03:04:05Z\"}", "MERGED")]
    [InlineData("open", "{}", "OPEN")]
    public async Task StateIsReportedFromTheStateAndMergeAsync(string state, string pullRequest, string expected)
    {
        PrSearchResult result = await this.SearchJsonAsync(
            Page(Item(number: 2, state: state, pullRequest: pullRequest))
        );

        Assert.Equal(expected: expected, actual: Assert.Single(result.PullRequests).State);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("null")]
    public async Task EmptyTitleIsShownAsPlaceholderAsync(string title)
    {
        PrSearchResult result = await this.SearchJsonAsync(Page(Item(number: 1, title: title)));

        Assert.Equal(expected: "-", actual: Assert.Single(result.PullRequests).Title);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"items\":null}")]
    [InlineData("{\"items\":[null]}")]
    [InlineData("{\"items\":[{\"state\":\"open\",\"title\":\"x\",\"pull_request\":{}}]}")]
    [InlineData("{\"items\":[{\"number\":1,\"state\":\"open\",\"title\":\"x\"}]}")]
    [InlineData("{\"items\":[{\"number\":1,\"state\":\"merged\",\"title\":\"x\",\"pull_request\":{}}]}")]
    [InlineData("{\"items\":[{\"number\":1,\"title\":\"x\",\"pull_request\":{}}]}")]
    public async Task InvalidResponseFailsTheSearchAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        PrSearchFailedException exception = await Assert.ThrowsAsync<PrSearchFailedException>(() =>
            this.SearchAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task NotFoundIsReportedAsMissingRepositoryAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(HttpStatusCode.NotFound));

        RepositoryNotFoundException exception = await Assert.ThrowsAsync<RepositoryNotFoundException>(() =>
            this.SearchAsync(upstream)
        );

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
    }

    [Fact]
    public async Task UpstreamErrorFailsTheSearchAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(HttpStatusCode.BadGateway));

        PrSearchFailedException exception = await Assert.ThrowsAsync<PrSearchFailedException>(() =>
            this.SearchAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData("credfeto")]
    [InlineData("credfeto/name/extra")]
    [InlineData("")]
    public async Task InvalidRepositoryIsRefusedWithoutRequestAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.SearchAsync(upstream: upstream, repository: repository)
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
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
