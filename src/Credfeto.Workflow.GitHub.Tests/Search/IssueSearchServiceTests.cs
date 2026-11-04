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

public sealed class IssueSearchServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string NOT_FOUND =
        "the repository credfeto/credfeto-workflow-mcp was not found, or the caller cannot see it";
    private const string FAILED = "could not search the issues of credfeto/credfeto-workflow-mcp";
    private const int LIMIT = 30;

    private static string Item(int number, string state = "open", string title = "\"A problem\"", string labels = "[]")
    {
        return "{\"number\":"
            + number.ToString(CultureInfo.InvariantCulture)
            + ",\"state\":\""
            + state
            + "\",\"title\":"
            + title
            + ",\"labels\":"
            + labels
            + "}";
    }

    private static string Page(params string[] items)
    {
        return "{\"total_count\":"
            + items.Length.ToString(CultureInfo.InvariantCulture)
            + ",\"items\":["
            + string.Join(separator: ',', value: items)
            + "]}";
    }

    private static string[] Items(IEnumerable<int> numbers)
    {
        return [.. numbers.Select(number => Item(number))];
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

    private IssueSearchService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(rest);
    }

    private async Task<IssueSearchResult> SearchAsync(
        RecordingHttpMessageHandler upstream,
        string text = "crash",
        IssueSearchState state = IssueSearchState.OPEN,
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

    private async Task<IssueSearchResult> SearchJsonAsync(string json, int limit = LIMIT)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        return await this.SearchAsync(upstream: upstream, limit: limit);
    }

    private static IReadOnlyList<int> Numbers(IssueSearchResult result)
    {
        return [.. result.Issues.Select(entry => entry.Number)];
    }

    [Theory]
    [InlineData(IssueSearchState.OPEN, "in:title,body crash repo:credfeto/credfeto-workflow-mcp type:issue state:open")]
    [InlineData(
        IssueSearchState.CLOSED,
        "in:title,body crash repo:credfeto/credfeto-workflow-mcp type:issue state:closed"
    )]
    [InlineData(IssueSearchState.ALL, "in:title,body crash repo:credfeto/credfeto-workflow-mcp type:issue")]
    public async Task SendsOneSearchScopedToTheRepositoryAsync(IssueSearchState state, string query)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Page(Item(1))));

        IssueSearchResult result = await this.SearchAsync(upstream: upstream, state: state);

        Assert.Equal(expected: REPOSITORY, actual: result.Repository);
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
        Assert.Equal(expected: SearchUri(query: query, perPage: LIMIT + 1), actual: request.RequestUri);
    }

    [Fact]
    public async Task QualifiersInTheTextCannotWidenTheSearchAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Page()));

        _ = await this.SearchAsync(upstream: upstream, text: "repo:evil/x type:pr");

        RecordedRequest request = Assert.Single(upstream.Requests);
        string uri = Assert.IsType<Uri>(request.RequestUri).AbsoluteUri;
        Assert.Single(Occurrences(text: uri, value: "repo%3A"));
        Assert.Single(Occurrences(text: uri, value: "type%3A"));
        Assert.Contains(
            expectedSubstring: "repo%3Acredfeto%2Fcredfeto-workflow-mcp",
            actualString: uri,
            StringComparison.Ordinal
        );
        Assert.Contains(expectedSubstring: "type%3Aissue", actualString: uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HashInTheTextIsEscapedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Page()));

        _ = await this.SearchAsync(upstream: upstream, text: "crash #12");

        RecordedRequest request = Assert.Single(upstream.Requests);
        Uri uri = Assert.IsType<Uri>(request.RequestUri);
        Assert.Empty(uri.Fragment);
        Assert.Contains(expectedSubstring: "%2312", actualString: uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LimitDecidesThePageSizeAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Page()));

        _ = await this.SearchAsync(upstream: upstream, limit: 100);

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.EndsWith(
            expectedEndString: "&per_page=101",
            actualString: Assert.IsType<Uri>(request.RequestUri).AbsoluteUri,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ExactlyTheLimitIsNotTruncatedAsync()
    {
        IssueSearchResult result = await this.SearchJsonAsync(json: Page(Item(9), Item(3), Item(5)), limit: 3);

        Assert.Equal(expected: [9, 3, 5], actual: Numbers(result));
        Assert.False(condition: result.Truncated, userMessage: "Exactly the limit should not be truncated");
    }

    [Fact]
    public async Task MoreThanTheLimitIsTruncatedInOrderAsync()
    {
        IssueSearchResult result = await this.SearchJsonAsync(json: Page(Item(9), Item(3), Item(5), Item(1)), limit: 3);

        Assert.Equal(expected: [9, 3, 5], actual: Numbers(result));
        Assert.True(condition: result.Truncated, userMessage: "More than the limit should be truncated");
    }

    [Fact]
    public async Task FullDefaultPageIsTruncatedAsync()
    {
        int[] numbers = [.. Enumerable.Range(start: 1, count: LIMIT + 1).Reverse()];

        IssueSearchResult result = await this.SearchJsonAsync(Page(Items(numbers)));

        Assert.Equal(expected: numbers.Take(LIMIT), actual: Numbers(result));
        Assert.True(condition: result.Truncated, userMessage: "A full page should be truncated");
    }

    [Fact]
    public async Task NoMatchIsEmptyAsync()
    {
        IssueSearchResult result = await this.SearchJsonAsync(Page());

        Assert.Equal(expected: REPOSITORY, actual: result.Repository);
        Assert.Empty(result.Issues);
        Assert.False(condition: result.Truncated, userMessage: "No match should not be truncated");
    }

    [Fact]
    public async Task ProjectsEveryFieldAsync()
    {
        IssueSearchResult result = await this.SearchJsonAsync(
            Page(Item(number: 7, title: "\"Crash on start\"", labels: """[{"name":"bug"},{"name":"Needs Review"}]"""))
        );

        IssueSearchEntry entry = Assert.Single(result.Issues);
        Assert.Equal(expected: 7, actual: entry.Number);
        Assert.Equal(expected: "OPEN", actual: entry.State);
        Assert.Equal(expected: "Crash on start", actual: entry.Title);
        Assert.Equal(expected: ["bug", "Needs Review"], actual: entry.Labels);
    }

    [Fact]
    public async Task ClosedStateIsReportedAsClosedAsync()
    {
        IssueSearchResult result = await this.SearchJsonAsync(Page(Item(number: 2, state: "closed")));

        Assert.Equal(expected: "CLOSED", actual: Assert.Single(result.Issues).State);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("null")]
    public async Task EmptyTitleIsShownAsPlaceholderAsync(string title)
    {
        IssueSearchResult result = await this.SearchJsonAsync(Page(Item(number: 1, title: title)));

        Assert.Equal(expected: "-", actual: Assert.Single(result.Issues).Title);
    }

    [Fact]
    public async Task MissingLabelsAreEmptyAsync()
    {
        IssueSearchResult result = await this.SearchJsonAsync(Page(Item(number: 1, labels: "null")));

        Assert.Empty(Assert.Single(result.Issues).Labels);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"items\":null}")]
    [InlineData("{\"items\":[null]}")]
    [InlineData("{\"items\":[{\"state\":\"open\",\"title\":\"x\"}]}")]
    [InlineData("{\"items\":[{\"number\":1,\"state\":\"open\",\"title\":\"x\",\"labels\":[null]}]}")]
    [InlineData("{\"items\":[{\"number\":1,\"state\":\"open\",\"title\":\"x\",\"labels\":[{}]}]}")]
    [InlineData("{\"items\":[{\"number\":1,\"state\":\"merged\",\"title\":\"x\"}]}")]
    [InlineData("{\"items\":[{\"number\":1,\"title\":\"x\"}]}")]
    public async Task InvalidResponseFailsTheSearchAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        IssueSearchFailedException exception = await Assert.ThrowsAsync<IssueSearchFailedException>(() =>
            this.SearchAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task HtmlResponseFailsTheSearchAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Html("<html></html>"));

        IssueSearchFailedException exception = await Assert.ThrowsAsync<IssueSearchFailedException>(() =>
            this.SearchAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
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

        IssueSearchFailedException exception = await Assert.ThrowsAsync<IssueSearchFailedException>(() =>
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
    [InlineData("crash AND boom")]
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

    private static IReadOnlyList<int> Occurrences(string text, string value)
    {
        List<int> found = [];
        int index = text.IndexOf(value: value, comparisonType: StringComparison.Ordinal);

        while (index >= 0)
        {
            found.Add(index);
            index = text.IndexOf(
                value: value,
                startIndex: index + value.Length,
                comparisonType: StringComparison.Ordinal
            );
        }

        return found;
    }
}
