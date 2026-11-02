using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
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

public sealed class IssueServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int ISSUE = 16;
    private const string REFERENCE = "credfeto/credfeto-workflow-mcp#16";
    private const string NOT_FOUND = "issue credfeto/credfeto-workflow-mcp#16 was not found";
    private const string FAILED = "could not read issue credfeto/credfeto-workflow-mcp#16";

    private const string FULL_ISSUE = """
        {"title":"Add issue view","state":"open","labels":[{"name":"feature"},{"name":"mcp"}],
        "assignees":[{"login":"credfeto"},{"login":"alice"}],"body":"Line one\nLine two",
        "html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/16"}
        """;

    private static readonly Uri IssueUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/16");

    private static string Mutate(string source, string from, string to)
    {
        Assert.Contains(expectedSubstring: from, actualString: source, comparisonType: StringComparison.Ordinal);

        return source.Replace(oldValue: from, newValue: to, comparisonType: StringComparison.Ordinal);
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private IssueService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(rest);
    }

    private async Task<IssueViewResponse> ViewAsync(RecordingHttpMessageHandler upstream)
    {
        return await this.CreateService(upstream)
            .ViewAsync(repository: REPOSITORY, issue: ISSUE, cancellationToken: this.CancellationToken());
    }

    private async Task<IssueViewResponse> ViewJsonAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        return await this.ViewAsync(upstream);
    }

    private Task<TException> ViewFailsAsync<TException>(RecordingHttpMessageHandler upstream)
        where TException : Exception
    {
        return Assert.ThrowsAsync<TException>(() => this.ViewAsync(upstream));
    }

    private async Task AssertInvalidResponseAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        IssueViewFailedException exception = await this.ViewFailsAsync<IssueViewFailedException>(upstream);

        Assert.Equal(expected: FAILED, actual: exception.Message);
        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    private async Task AssertRejectedAsync(string repository, int issue, string parameterName)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateService(upstream)
                .ViewAsync(repository: repository, issue: issue, cancellationToken: this.CancellationToken())
                .AsTask()
        );

        Assert.Equal(expected: parameterName, actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("credfeto")]
    [InlineData("credfeto/")]
    [InlineData("/credfeto-workflow-mcp")]
    [InlineData("credfeto/credfeto-workflow-mcp/extra")]
    public Task MalformedRepositoryIsRejectedBeforeAnyRequestAsync(string repository)
    {
        return this.AssertRejectedAsync(repository: repository, issue: ISSUE, parameterName: "repository");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public Task NonPositiveIssueIsRejectedBeforeAnyRequestAsync(int issue)
    {
        return this.AssertRejectedAsync(repository: REPOSITORY, issue: issue, parameterName: "issue");
    }

    [Fact]
    public async Task OpenIssueIsReturnedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json(FULL_ISSUE));

        IssueViewResponse response = await this.ViewAsync(upstream);

        Assert.Equal(expected: REFERENCE, actual: response.Issue);
        Assert.Equal(expected: "Add issue view", actual: response.Title);
        Assert.Equal(expected: "OPEN", actual: response.State);
        Assert.Equal(expected: ["feature", "mcp"], actual: response.Labels);
        Assert.Equal(expected: ["credfeto", "alice"], actual: response.Assignees);
        Assert.Equal(expected: "Line one\nLine two", actual: response.Body);

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
        Assert.Equal(expected: IssueUri, actual: request.RequestUri);
    }

    [Fact]
    public async Task ClosedIssueIsReturnedAsync()
    {
        IssueViewResponse response = await this.ViewJsonAsync(
            Mutate(source: FULL_ISSUE, from: "\"state\":\"open\"", to: "\"state\":\"closed\"")
        );

        Assert.Equal(expected: "CLOSED", actual: response.State);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task EmptyOrNullLabelsAndAssigneesAreEmptyAsync(string list)
    {
        string json = Mutate(
            source: Mutate(source: FULL_ISSUE, from: """[{"name":"feature"},{"name":"mcp"}]""", to: list),
            from: """[{"login":"credfeto"},{"login":"alice"}]""",
            to: list
        );

        IssueViewResponse response = await this.ViewJsonAsync(json);

        Assert.Empty(response.Labels);
        Assert.Empty(response.Assignees);
    }

    [Fact]
    public async Task MissingOptionalFieldsAreEmptyAsync()
    {
        IssueViewResponse response = await this.ViewJsonAsync("""{"title":"Add issue view","state":"open"}""");

        Assert.Equal(expected: "Add issue view", actual: response.Title);
        Assert.Empty(response.Labels);
        Assert.Empty(response.Assignees);
        Assert.Equal(expected: string.Empty, actual: response.Body);
    }

    [Fact]
    public async Task NullBodyIsEmptyAsync()
    {
        IssueViewResponse response = await this.ViewJsonAsync(
            Mutate(source: FULL_ISSUE, from: "\"body\":\"Line one\\nLine two\"", to: "\"body\":null")
        );

        Assert.Equal(expected: string.Empty, actual: response.Body);
    }

    [Fact]
    public async Task LineBreakingCharactersAreFlattenedOutsideTheBodyAsync()
    {
        string json = Mutate(
            source: Mutate(
                source: Mutate(source: FULL_ISSUE, from: "Add issue view", to: "Add\\nissue\\u2028view"),
                from: "{\"name\":\"feature\"}",
                to: "{\"name\":\"fea\\rture\"}"
            ),
            from: "{\"login\":\"alice\"}",
            to: "{\"login\":\"ali\\tce\"}"
        );

        IssueViewResponse response = await this.ViewJsonAsync(json);

        Assert.Equal(expected: "Add issue view", actual: response.Title);
        Assert.Equal(expected: ["fea ture", "mcp"], actual: response.Labels);
        Assert.Equal(expected: ["credfeto", "ali ce"], actual: response.Assignees);
        Assert.Equal(expected: "Line one\nLine two", actual: response.Body);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"name\":null}")]
    [InlineData("{}")]
    public Task LabelWithoutNameIsInvalidAsync(string label)
    {
        return this.AssertInvalidResponseAsync(Mutate(source: FULL_ISSUE, from: "{\"name\":\"mcp\"}", to: label));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"login\":null}")]
    [InlineData("{}")]
    public Task AssigneeWithoutLoginIsInvalidAsync(string assignee)
    {
        return this.AssertInvalidResponseAsync(Mutate(source: FULL_ISSUE, from: "{\"login\":\"alice\"}", to: assignee));
    }

    [Theory]
    [InlineData("\"title\":null")]
    [InlineData("\"other\":\"value\"")]
    public Task MissingTitleIsInvalidAsync(string title)
    {
        return this.AssertInvalidResponseAsync(
            Mutate(source: FULL_ISSUE, from: "\"title\":\"Add issue view\"", to: title)
        );
    }

    [Theory]
    [InlineData("\"state\":\"merged\"")]
    [InlineData("\"state\":\"OPEN\"")]
    [InlineData("\"state\":null")]
    [InlineData("\"other\":\"value\"")]
    public Task UnknownStateIsInvalidAsync(string state)
    {
        return this.AssertInvalidResponseAsync(Mutate(source: FULL_ISSUE, from: "\"state\":\"open\"", to: state));
    }

    [Fact]
    public async Task PullRequestLinkIsNotFoundAsync()
    {
        string json = Mutate(
            source: FULL_ISSUE,
            from: "\"title\":",
            to: "\"pull_request\":{\"url\":\"https://api.github.com/repos/x/y/pulls/16\"},\"title\":"
        );
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        IssueNotFoundException exception = await this.ViewFailsAsync<IssueNotFoundException>(upstream);

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
    }

    [Fact]
    public async Task EmptyPullRequestLinkIsNotFoundAsync()
    {
        string json = Mutate(source: FULL_ISSUE, from: "\"title\":", to: "\"pull_request\":{},\"title\":");
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        IssueNotFoundException exception = await this.ViewFailsAsync<IssueNotFoundException>(upstream);

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
    }

    [Fact]
    public async Task PullRequestUrlIsNotFoundAsync()
    {
        string json = Mutate(source: FULL_ISSUE, from: "/issues/16", to: "/pull/16");
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        IssueNotFoundException exception = await this.ViewFailsAsync<IssueNotFoundException>(upstream);

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
    }

    [Fact]
    public async Task MissingIssueIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        IssueNotFoundException exception = await this.ViewFailsAsync<IssueNotFoundException>(upstream);

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.NotFound, actual: inner.StatusCode);
    }

    [Fact]
    public async Task HttpFailureIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        IssueViewFailedException exception = await this.ViewFailsAsync<IssueViewFailedException>(upstream);

        Assert.Equal(expected: FAILED, actual: exception.Message);
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task UnreadableBodyIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Html("<html></html>"));

        IssueViewFailedException exception = await this.ViewFailsAsync<IssueViewFailedException>(upstream);

        Assert.Equal(expected: FAILED, actual: exception.Message);
        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public Task MalformedJsonIsFailedAsync()
    {
        return this.AssertInvalidResponseAsync("not json");
    }

    [Fact]
    public async Task CancellationIsNotWrappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json(FULL_ISSUE));
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            this.CreateService(upstream)
                .ViewAsync(repository: REPOSITORY, issue: ISSUE, cancellationToken: cancelled.Token)
                .AsTask()
        );
    }
}
