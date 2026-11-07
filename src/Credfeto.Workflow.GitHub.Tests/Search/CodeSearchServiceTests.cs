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

public sealed class CodeSearchServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string NOT_FOUND =
        "the repository credfeto/credfeto-workflow-mcp was not found, or the caller cannot see it";
    private const string FAILED = "could not search the code of credfeto/credfeto-workflow-mcp";
    private const string TEXT_MATCH_MEDIA_TYPE = "application/vnd.github.text-match+json";
    private const int LIMIT = 30;
    private const string ONE_MATCH = "[{\"matches\":[{\"indices\":[0,5]}]}]";
    private const string REPOSITORY_JSON =
        "{\"full_name\":\"credfeto/credfeto-workflow-mcp\",\"default_branch\":\"main\"}";

    private static string Item(string path, string textMatches = ONE_MATCH)
    {
        return "{\"path\":\"" + path + "\",\"text_matches\":" + textMatches + "}";
    }

    private static string Page(params string[] items)
    {
        return PageOf(totalCount: items.Length.ToString(CultureInfo.InvariantCulture), items: items);
    }

    private static string PageOf(string totalCount, params string[] items)
    {
        return "{\"total_count\":" + totalCount + ",\"items\":[" + string.Join(separator: ',', value: items) + "]}";
    }

    private static Func<int, HttpResponseMessage> RepositoryThen(Func<HttpResponseMessage> search)
    {
        return index => index == 0 ? Responses.Json(REPOSITORY_JSON) : search();
    }

    private static Uri SearchUri(string query, int perPage)
    {
        return new(
            GitHubServices.ApiBaseUrl
                + "search/code?q="
                + Uri.EscapeDataString(query)
                + "&per_page="
                + perPage.ToString(CultureInfo.InvariantCulture)
        );
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private CodeSearchService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(rest);
    }

    private async Task<CodeSearchResult> SearchAsync(
        RecordingHttpMessageHandler upstream,
        string text = "crash",
        int limit = LIMIT,
        string repository = REPOSITORY
    )
    {
        return await this.CreateService(upstream)
            .SearchAsync(repository: repository, text: text, limit: limit, cancellationToken: this.CancellationToken());
    }

    private async Task<CodeSearchResult> SearchJsonAsync(string json, int limit = LIMIT)
    {
        using RecordingHttpMessageHandler upstream = new(RepositoryThen(() => Responses.Json(json)));

        return await this.SearchAsync(upstream: upstream, limit: limit);
    }

    private static IReadOnlyList<string> Paths(CodeSearchResult result)
    {
        return [.. result.Files.Select(entry => entry.Path)];
    }

    [Fact]
    public async Task ReadsTheRepositoryThenSearchesItsCodeAsync()
    {
        using RecordingHttpMessageHandler upstream = new(RepositoryThen(() => Responses.Json(Page(Item("a.cs")))));

        CodeSearchResult result = await this.SearchAsync(upstream);

        Assert.Equal(expected: REPOSITORY, actual: result.Repository);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        RecordedRequest repositoryRequest = upstream.Requests[0];
        Assert.Equal(expected: HttpMethod.Get, actual: repositoryRequest.Method);
        Assert.Equal(
            expected: new Uri(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY),
            actual: repositoryRequest.RequestUri
        );
        RecordedRequest searchRequest = upstream.Requests[1];
        Assert.Equal(expected: HttpMethod.Get, actual: searchRequest.Method);
        Assert.Equal(
            expected: SearchUri(query: "crash repo:" + REPOSITORY, perPage: LIMIT + 1),
            actual: searchRequest.RequestUri
        );
        Assert.Equal(expected: TEXT_MATCH_MEDIA_TYPE, actual: searchRequest.Accept);
    }

    [Fact]
    public async Task QualifiersInTheTextCannotWidenTheSearchAsync()
    {
        using RecordingHttpMessageHandler upstream = new(RepositoryThen(() => Responses.Json(Page())));

        _ = await this.SearchAsync(upstream: upstream, text: "repo:evil/x path:secrets");

        string uri = Assert.IsType<Uri>(upstream.Requests[1].RequestUri).AbsoluteUri;
        Assert.Contains(
            expectedSubstring: "repo%3Acredfeto%2Fcredfeto-workflow-mcp",
            actualString: uri,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(expectedSubstring: "repo%3Aevil", actualString: uri, StringComparison.Ordinal);
        Assert.DoesNotContain(expectedSubstring: "path%3A", actualString: uri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "&per_page=2")]
    [InlineData(100, "&per_page=100")]
    public async Task LimitDecidesThePageSizeAsync(int limit, string perPage)
    {
        using RecordingHttpMessageHandler upstream = new(RepositoryThen(() => Responses.Json(Page())));

        _ = await this.SearchAsync(upstream: upstream, limit: limit);

        Assert.EndsWith(
            expectedEndString: perPage,
            actualString: Assert.IsType<Uri>(upstream.Requests[1].RequestUri).AbsoluteUri,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task FilesAreSortedByPathAsync()
    {
        CodeSearchResult result = await this.SearchJsonAsync(
            Page(Item("src/b.cs"), Item("README.md"), Item("src/a.cs"))
        );

        Assert.Equal(expected: ["README.md", "src/a.cs", "src/b.cs"], actual: Paths(result));
        Assert.False(condition: result.Truncated, userMessage: "Exactly the matches should not be truncated");
    }

    [Fact]
    public async Task MoreThanTheLimitIsTruncatedAsync()
    {
        CodeSearchResult result = await this.SearchJsonAsync(
            json: Page(Item("d.cs"), Item("c.cs"), Item("b.cs"), Item("a.cs")),
            limit: 3
        );

        Assert.Equal(expected: ["b.cs", "c.cs", "d.cs"], actual: Paths(result));
        Assert.True(condition: result.Truncated, userMessage: "More than the limit should be truncated");
    }

    [Fact]
    public async Task TotalCountAboveTheLimitIsTruncatedAsync()
    {
        CodeSearchResult result = await this.SearchJsonAsync(json: PageOf(totalCount: "4", Item("a.cs")), limit: 3);

        Assert.True(condition: result.Truncated, userMessage: "A total count above the limit should be truncated");
    }

    [Fact]
    public async Task NoMatchIsEmptyAsync()
    {
        CodeSearchResult result = await this.SearchJsonAsync(Page());

        Assert.Equal(expected: REPOSITORY, actual: result.Repository);
        Assert.Empty(result.Files);
        Assert.False(condition: result.Truncated, userMessage: "No match should not be truncated");
    }

    [Theory]
    [InlineData("[]", 0)]
    [InlineData(ONE_MATCH, 1)]
    [InlineData("[{\"matches\":[{\"indices\":[0,5]},{\"indices\":[9,14]}]},{\"matches\":[{}]}]", 3)]
    public async Task MatchesAreCountedAcrossTextMatchesAsync(string textMatches, int expected)
    {
        CodeSearchResult result = await this.SearchJsonAsync(Page(Item(path: "a.cs", textMatches: textMatches)));

        CodeSearchEntry entry = Assert.Single(result.Files);
        Assert.Equal(expected: "a.cs", actual: entry.Path);
        Assert.Equal(expected: expected, actual: entry.Matches);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"items\":null}")]
    [InlineData("{\"items\":[null]}")]
    [InlineData("{\"items\":[{\"text_matches\":[]}]}")]
    [InlineData("{\"items\":[{\"path\":\"  \",\"text_matches\":[]}]}")]
    [InlineData("{\"items\":[{\"path\":\"a.cs\"}]}")]
    [InlineData("{\"items\":[{\"path\":\"a.cs\",\"text_matches\":[null]}]}")]
    [InlineData("{\"items\":[{\"path\":\"a.cs\",\"text_matches\":[{}]}]}")]
    public async Task InvalidResponseFailsTheSearchAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(RepositoryThen(() => Responses.Json(json)));

        CodeSearchFailedException exception = await Assert.ThrowsAsync<CodeSearchFailedException>(() =>
            this.SearchAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task MissingRepositoryIsReportedWithoutSearchingAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(HttpStatusCode.NotFound));

        RepositoryNotFoundException exception = await Assert.ThrowsAsync<RepositoryNotFoundException>(() =>
            this.SearchAsync(upstream)
        );

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task SearchErrorFailsTheSearchAsync(HttpStatusCode status)
    {
        using RecordingHttpMessageHandler upstream = new(RepositoryThen(() => Responses.Status(status)));

        CodeSearchFailedException exception = await Assert.ThrowsAsync<CodeSearchFailedException>(() =>
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
    [InlineData("NOT crash")]
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
