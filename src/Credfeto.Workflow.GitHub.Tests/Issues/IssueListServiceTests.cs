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
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Issues;

public sealed class IssueListServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string NOT_FOUND =
        "the repository credfeto/credfeto-workflow-mcp was not found, or the caller cannot see it";
    private const string FAILED = "could not list the open issues of credfeto/credfeto-workflow-mcp";
    private const string TOO_MANY = "credfeto/credfeto-workflow-mcp has more than 1000 open issues to list";

    private static readonly Uri ListUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues?state=open&per_page=100"
    );

    private static string Item(
        int number,
        string labels = "",
        string assignees = "",
        string title = "\"An issue\"",
        string extra = ""
    )
    {
        return "{\"number\":"
            + number.ToString(CultureInfo.InvariantCulture)
            + ",\"title\":"
            + title
            + ",\"labels\":["
            + Objects(names: labels, property: "name")
            + "],\"assignees\":["
            + Objects(names: assignees, property: "login")
            + "]"
            + extra
            + "}";
    }

    private static string Objects(string names, string property)
    {
        return string.Join(
            separator: ',',
            values: names
                .Split(separator: ',', options: StringSplitOptions.RemoveEmptyEntries)
                .Select(name => "{\"" + property + "\":\"" + name + "\"}")
        );
    }

    private static string Page(params string[] items)
    {
        return "[" + string.Join(separator: ',', value: items) + "]";
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private IssueListService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(rest);
    }

    private async Task<IssueListResponse> ListAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY
    )
    {
        return await this.CreateService(upstream)
            .ListOpenIssuesAsync(repository: repository, cancellationToken: this.CancellationToken());
    }

    private async Task<IssueListResponse> ListJsonAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        return await this.ListAsync(upstream);
    }

    private static IReadOnlyList<int> Numbers(IssueListResponse response)
    {
        return [.. response.Issues.Select(entry => entry.Number)];
    }

    [Fact]
    public async Task ReadsTheOpenIssuesOfTheRepositoryAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Page(Item(1))));

        IssueListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
        Assert.Equal(expected: ListUri, actual: request.RequestUri);
    }

    [Fact]
    public async Task NoOpenIssuesIsEmptyAsync()
    {
        IssueListResponse response = await this.ListJsonAsync("[]");

        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        Assert.Empty(response.Issues);
    }

    [Fact]
    public async Task ProjectsEveryFieldAsync()
    {
        IssueListResponse response = await this.ListJsonAsync(
            Page(Item(number: 7, labels: "bug,High", assignees: "credfeto,alice", title: "\"Add list\""))
        );

        IssueSummary issue = Assert.Single(response.Issues);
        Assert.Equal(expected: 7, actual: issue.Number);
        Assert.Equal(expected: "High", actual: issue.Priority);
        Assert.False(condition: issue.Blocked, userMessage: "Blocked");
        Assert.False(condition: issue.OnHold, userMessage: "OnHold");
        Assert.Equal(expected: ["credfeto", "alice"], actual: issue.Assignees);
        Assert.Equal(expected: "Add list", actual: issue.Title);
        Assert.Equal(expected: ["bug", "High"], actual: issue.Labels);
    }

    [Fact]
    public async Task IssuesAreSortedByPriorityThenNumberAsync()
    {
        IssueListResponse response = await this.ListJsonAsync(
            Page(
                Item(number: 9, labels: "Low"),
                Item(number: 8),
                Item(number: 7, labels: "Medium"),
                Item(number: 6, labels: "High"),
                Item(number: 5, labels: "Urgent"),
                Item(number: 4, labels: "Security"),
                Item(number: 3, labels: "High"),
                Item(number: 2)
            )
        );

        Assert.Equal(expected: [4, 5, 3, 6, 7, 9, 2, 8], actual: Numbers(response));
    }

    [Fact]
    public async Task IssueWithoutPriorityHasNoneAsync()
    {
        IssueListResponse response = await this.ListJsonAsync(Page(Item(number: 1, labels: "bug")));

        Assert.Null(Assert.Single(response.Issues).Priority);
    }

    [Fact]
    public async Task PriorityIsReportedInItsCanonicalSpellingAsync()
    {
        IssueListResponse response = await this.ListJsonAsync(Page(Item(number: 1, labels: "uRgEnT")));

        IssueSummary issue = Assert.Single(response.Issues);
        Assert.Equal(expected: "Urgent", actual: issue.Priority);
        Assert.Equal(expected: ["uRgEnT"], actual: issue.Labels);
    }

    [Fact]
    public async Task MostUrgentOfSeveralPrioritiesWinsAsync()
    {
        IssueListResponse response = await this.ListJsonAsync(Page(Item(number: 1, labels: "Low,security,High")));

        Assert.Equal(expected: "Security", actual: Assert.Single(response.Issues).Priority);
    }

    [Theory]
    [InlineData("Blocked")]
    [InlineData("blocked")]
    [InlineData("BLOCKED")]
    public async Task BlockedLabelFlagsTheIssueAsync(string label)
    {
        IssueListResponse response = await this.ListJsonAsync(Page(Item(number: 1, labels: "bug," + label)));

        IssueSummary issue = Assert.Single(response.Issues);
        Assert.True(condition: issue.Blocked, userMessage: label);
        Assert.False(condition: issue.OnHold, userMessage: label);
    }

    [Theory]
    [InlineData("On Hold")]
    [InlineData("on hold")]
    [InlineData("On-Hold")]
    [InlineData("ON-HOLD")]
    public async Task OnHoldLabelFlagsTheIssueAsync(string label)
    {
        IssueListResponse response = await this.ListJsonAsync(Page(Item(number: 1, labels: label)));

        IssueSummary issue = Assert.Single(response.Issues);
        Assert.True(condition: issue.OnHold, userMessage: label);
        Assert.False(condition: issue.Blocked, userMessage: label);
    }

    [Theory]
    [InlineData("Blocked-ish")]
    [InlineData("OnHold")]
    [InlineData("On_Hold")]
    public async Task SimilarLabelsDoNotFlagTheIssueAsync(string label)
    {
        IssueListResponse response = await this.ListJsonAsync(Page(Item(number: 1, labels: label)));

        IssueSummary issue = Assert.Single(response.Issues);
        Assert.False(condition: issue.Blocked, userMessage: label);
        Assert.False(condition: issue.OnHold, userMessage: label);
    }

    [Fact]
    public async Task PullRequestsAreExcludedAsync()
    {
        IssueListResponse response = await this.ListJsonAsync(
            Page(
                Item(number: 1),
                Item(number: 2, extra: ",\"pull_request\":{\"url\":\"https://api.github.com/pulls/2\"}"),
                Item(number: 3, extra: ",\"pull_request\":{}"),
                Item(number: 4, extra: ",\"pull_request\":null")
            )
        );

        Assert.Equal(expected: [1, 4], actual: Numbers(response));
    }

    [Fact]
    public async Task MoreThanTheCapIsRefusedAsync()
    {
        string json = Page([.. Enumerable.Range(start: 1, count: 1001).Select(number => Item(number))]);

        IssueListFailedException exception = await Assert.ThrowsAsync<IssueListFailedException>(() =>
            this.ListJsonAsync(json)
        );

        Assert.Equal(expected: TOO_MANY, actual: exception.Message);
    }

    [Fact]
    public async Task ExactlyTheCapIsListedAsync()
    {
        string json = Page([.. Enumerable.Range(start: 1, count: 1000).Select(number => Item(number))]);

        IssueListResponse response = await this.ListJsonAsync(json);

        Assert.Equal(expected: 1000, actual: response.Issues.Count);
    }

    [Fact]
    public async Task CapCountsOnlyIssuesAsync()
    {
        string json = Page([
            .. Enumerable
                .Range(start: 1, count: 1001)
                .Select(number => Item(number: number, extra: number == 1 ? ",\"pull_request\":{}" : string.Empty)),
        ]);

        IssueListResponse response = await this.ListJsonAsync(json);

        Assert.Equal(expected: 1000, actual: response.Issues.Count);
    }

    [Fact]
    public async Task LineBreakingCharactersAreFlattenedAsync()
    {
        IssueListResponse response = await this.ListJsonAsync(
            Page(
                Item(
                    number: 1,
                    labels: "bad\\nlabel",
                    assignees: "bad\\u0007login",
                    title: "\"first\\r\\nsecond\\u2028third\""
                )
            )
        );

        IssueSummary issue = Assert.Single(response.Issues);
        Assert.Equal(expected: "first  second third", actual: issue.Title);
        Assert.Equal(expected: ["bad label"], actual: issue.Labels);
        Assert.Equal(expected: ["bad login"], actual: issue.Assignees);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("\"\\n\"")]
    public async Task EmptyTitleIsShownAsPlaceholderAsync(string title)
    {
        IssueListResponse response = await this.ListJsonAsync(Page(Item(number: 1, title: title)));

        Assert.Equal(expected: "-", actual: Assert.Single(response.Issues).Title);
    }

    [Theory]
    [InlineData("""[{"number":1}]""")]
    [InlineData("""[{"number":1,"labels":null,"assignees":null}]""")]
    public async Task MissingListsAreEmptyAsync(string json)
    {
        IssueListResponse response = await this.ListJsonAsync(json);

        IssueSummary issue = Assert.Single(response.Issues);
        Assert.Empty(issue.Labels);
        Assert.Empty(issue.Assignees);
        Assert.Null(issue.Priority);
        Assert.Equal(expected: "-", actual: issue.Title);
    }

    [Fact]
    public async Task EveryPageIsReadAsync()
    {
        const string secondPage = GitHubServices.ApiBaseUrl + "repositories/1/issues?state=open&per_page=100&page=2";
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(json: Page(Item(4)), nextLink: "<" + secondPage + ">; rel=\"next\""),
                1 => Responses.Json(Page(Item(2))),
                _ => Unexpected(index),
            }
        );

        IssueListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: [2, 4], actual: Numbers(response));
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task MissingRepositoryIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        RepositoryNotFoundException exception = await Assert.ThrowsAsync<RepositoryNotFoundException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.NotFound, actual: inner.StatusCode);
    }

    [Fact]
    public async Task HttpFailureIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        IssueListFailedException exception = await Assert.ThrowsAsync<IssueListFailedException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData("<html></html>")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("""[{"title":"no number"}]""")]
    [InlineData("""[{"number":1,"labels":[null]}]""")]
    [InlineData("""[{"number":1,"labels":[{}]}]""")]
    [InlineData("""[{"number":1,"assignees":[null]}]""")]
    [InlineData("""[{"number":1,"assignees":[{}]}]""")]
    public async Task InvalidResponseIsFailedAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            json[0] == '<' ? Responses.Html(json) : Responses.Json(json)
        );

        IssueListFailedException exception = await Assert.ThrowsAsync<IssueListFailedException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Theory]
    [InlineData("")]
    [InlineData("credfeto")]
    [InlineData("credfeto/credfeto-workflow-mcp/extra")]
    [InlineData("credfeto/bad name")]
    public async Task InvalidRepositoryIsRejectedWithoutRequestAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ListAsync(upstream: upstream, repository: repository)
        );

        Assert.Contains(
            expectedSubstring: "repository",
            actualString: exception.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Empty(upstream.Requests);
    }
}
