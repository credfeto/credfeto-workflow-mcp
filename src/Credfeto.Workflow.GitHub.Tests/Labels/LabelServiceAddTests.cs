using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Labels;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Labels;

public sealed class LabelServiceAddTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string FAILED = "could not change the labels of credfeto/credfeto-workflow-mcp#5";

    private static readonly Uri LabelsUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/5/labels");

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private Task<LabelChangeResponse> AddAsync(
        RecordingHttpMessageHandler upstream,
        IReadOnlyList<string> labels,
        int? pullRequest = 5,
        int? issue = null,
        string repository = REPOSITORY
    )
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );
        LabelService service = new(rest);

        return service
            .AddAsync(
                repository: repository,
                pullRequest: pullRequest,
                issue: issue,
                labels: labels,
                cancellationToken: this.CancellationToken()
            )
            .AsTask();
    }

    [Fact]
    public async Task AddsEachLabelInItsOwnRequestAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json("""[{"name":"Bug"}]"""),
                1 => Responses.Json("""[{"name":"Bug"},{"name":"Tech Debt"}]"""),
                _ => Unexpected(index),
            }
        );

        LabelChangeResponse response = await this.AddAsync(upstream: upstream, labels: ["Bug", "Tech Debt"]);

        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        Assert.Equal(expected: 5, actual: response.Number);
        Assert.Equal(expected: ["Bug", "Tech Debt"], actual: response.Labels);
        Assert.Collection(
            upstream.Requests,
            request => AssertPost(request: request, body: """{"labels":["Bug"]}"""),
            request => AssertPost(request: request, body: """{"labels":["Tech Debt"]}""")
        );
    }

    private static void AssertPost(RecordedRequest request, string body)
    {
        Assert.Equal(expected: HttpMethod.Post, actual: request.Method);
        Assert.Equal(expected: LabelsUri, actual: request.RequestUri);
        Assert.Equal(expected: body, actual: request.Body);
    }

    [Fact]
    public async Task AddsToAnIssueAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json("""[{"name":"Bug"}]"""));

        LabelChangeResponse response = await this.AddAsync(
            upstream: upstream,
            labels: ["Bug"],
            pullRequest: null,
            issue: 5
        );

        Assert.Equal(expected: ["Bug"], actual: response.Labels);
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: LabelsUri, actual: request.RequestUri);
    }

    [Fact]
    public async Task DuplicateLabelsAreAddedOnceInTheirStandardSpellingAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json("""[{"name":"Bug"}]"""));

        _ = await this.AddAsync(upstream: upstream, labels: ["bug", "BUG"]);

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: """{"labels":["Bug"]}""", actual: request.Body);
    }

    [Fact]
    public async Task MissingPullRequestIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        PullRequestNotFoundException exception = await Assert.ThrowsAsync<PullRequestNotFoundException>(() =>
            this.AddAsync(upstream: upstream, labels: ["Bug"])
        );

        Assert.Equal(
            expected: "the pull request credfeto/credfeto-workflow-mcp#5 was not found, or the caller cannot see it",
            actual: exception.Message
        );
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.NotFound, actual: inner.StatusCode);
    }

    [Fact]
    public async Task MissingIssueIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        IssueNotFoundException exception = await Assert.ThrowsAsync<IssueNotFoundException>(() =>
            this.AddAsync(upstream: upstream, labels: ["Bug"], pullRequest: null, issue: 5)
        );

        Assert.Equal(
            expected: "the issue credfeto/credfeto-workflow-mcp#5 was not found, or the caller cannot see it",
            actual: exception.Message
        );
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task UpstreamFailureFailsTheChangeAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        LabelChangeFailedException exception = await Assert.ThrowsAsync<LabelChangeFailedException>(() =>
            this.AddAsync(upstream: upstream, labels: ["Bug"])
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    public async Task InvalidResponseFailsTheChangeAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        LabelChangeFailedException exception = await Assert.ThrowsAsync<LabelChangeFailedException>(() =>
            this.AddAsync(upstream: upstream, labels: ["Bug"])
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Theory]
    [InlineData(null, null, "pr")]
    [InlineData(5, 6, "pr")]
    [InlineData(0, null, "pullRequest")]
    [InlineData(-1, null, "pullRequest")]
    [InlineData(null, 0, "issue")]
    [InlineData(null, -1, "issue")]
    public async Task InvalidTargetIsRejectedAsync(int? pullRequest, int? issue, string parameterName)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.AddAsync(upstream: upstream, labels: ["Bug"], pullRequest: pullRequest, issue: issue)
        );

        Assert.Equal(expected: parameterName, actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("credfeto")]
    [InlineData("credfeto/credfeto-workflow-mcp/extra")]
    public async Task InvalidRepositoryIsRejectedAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.AddAsync(upstream: upstream, labels: ["Bug"], repository: repository)
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task NoLabelsAreRejectedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.AddAsync(upstream: upstream, labels: [])
        );

        Assert.Equal(expected: "labels", actual: exception.ParameterName);
        Assert.Equal(expected: "at least one label must be given", actual: exception.Rule);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Bug,Enhancement")]
    [InlineData("Bug\n")]
    [InlineData("Not A Standard Label")]
    [InlineData("credfeto-workflow-mcp")]
    public async Task InvalidLabelIsRejectedAsync(string label)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.AddAsync(upstream: upstream, labels: ["Bug", label])
        );

        Assert.Equal(expected: "labels", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("Security")]
    [InlineData("Urgent")]
    [InlineData("High")]
    [InlineData("medium")]
    [InlineData("Low")]
    public async Task PriorityLabelIsRefusedAsync(string label)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        LabelRefusedException exception = await Assert.ThrowsAsync<LabelRefusedException>(() =>
            this.AddAsync(upstream: upstream, labels: ["Bug", label])
        );

        Assert.Equal(expected: label, actual: exception.Label, ignoreCase: true);
        Assert.Equal(expected: "a priority label may only be changed by a human", actual: exception.Rule);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("Blocked")]
    [InlineData("On Hold")]
    public async Task BlockingLabelsCanBeAddedAsync(string label)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json("[{\"name\":\"" + label + "\"}]"));

        LabelChangeResponse response = await this.AddAsync(upstream: upstream, labels: [label]);

        Assert.Equal(expected: [label], actual: response.Labels);
    }
}
