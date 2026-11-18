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

public sealed class LabelServiceIssueOnlyTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";

    private const string ISSUE_URI = GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/5";

    private const string ISSUE_JSON =
        "{\"id\":5,\"title\":\"t\",\"html_url\":\"https://github.com/credfeto/credfeto-workflow-mcp/issues/5\"}";

    private const string CHECK_FAILED =
        "could not check that credfeto/credfeto-workflow-mcp#5 is an issue, so no label was changed";

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private async Task<LabelChangeResponse> ChangeAsync(
        RecordingHttpMessageHandler upstream,
        bool removing,
        IReadOnlyList<string> labels,
        int? issue = 5,
        string repository = REPOSITORY
    )
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );
        LabelService service = new(rest);

        if (removing)
        {
            return await service.RemoveFromIssueAsync(
                repository: repository,
                issue: issue,
                labels: labels,
                cancellationToken: this.CancellationToken()
            );
        }

        return await service.AddToIssueAsync(
            repository: repository,
            issue: issue,
            labels: labels,
            cancellationToken: this.CancellationToken()
        );
    }

    private static void AssertRequest(RecordedRequest request, HttpMethod method, string uri)
    {
        Assert.Equal(expected: method, actual: request.Method);
        Assert.Equal(expected: new Uri(uri), actual: request.RequestUri);
    }

    [Fact]
    public async Task AddsToAnIssueAfterCheckingItIsAnIssueAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(ISSUE_JSON),
                1 => Responses.Json("""[{"name":"Bug"}]"""),
                _ => Unexpected(index),
            }
        );

        LabelChangeResponse response = await this.ChangeAsync(upstream: upstream, removing: false, labels: ["Bug"]);

        Assert.Equal(expected: 5, actual: response.Number);
        Assert.Equal(expected: ["Bug"], actual: response.Labels);
        Assert.Collection(
            upstream.Requests,
            request => AssertRequest(request: request, method: HttpMethod.Get, uri: ISSUE_URI),
            request => AssertRequest(request: request, method: HttpMethod.Post, uri: ISSUE_URI + "/labels")
        );
    }

    [Fact]
    public async Task RemovesFromAnIssueAfterCheckingItIsAnIssueAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(ISSUE_JSON),
                1 => Responses.Json("""[{"name":"Bug"},{"name":"Enhancement"}]"""),
                2 => Responses.Json("""[{"name":"Enhancement"}]"""),
                _ => Unexpected(index),
            }
        );

        LabelChangeResponse response = await this.ChangeAsync(upstream: upstream, removing: true, labels: ["Bug"]);

        Assert.Equal(expected: ["Enhancement"], actual: response.Labels);
        Assert.Collection(
            upstream.Requests,
            request => AssertRequest(request: request, method: HttpMethod.Get, uri: ISSUE_URI),
            request => AssertRequest(request: request, method: HttpMethod.Get, uri: ISSUE_URI + "/labels?per_page=100"),
            request => AssertRequest(request: request, method: HttpMethod.Delete, uri: ISSUE_URI + "/labels/Bug")
        );
    }

    [Theory]
    [InlineData(
        false,
        "{\"id\":5,\"title\":\"t\",\"pull_request\":{\"url\":\"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/5\"}}"
    )]
    [InlineData(
        true,
        "{\"id\":5,\"title\":\"t\",\"pull_request\":{\"url\":\"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/5\"}}"
    )]
    [InlineData(
        false,
        "{\"id\":5,\"title\":\"t\",\"html_url\":\"https://github.com/credfeto/credfeto-workflow-mcp/pull/5\"}"
    )]
    [InlineData(
        true,
        "{\"id\":5,\"title\":\"t\",\"html_url\":\"https://github.com/credfeto/credfeto-workflow-mcp/pull/5\"}"
    )]
    public async Task PullRequestIsRefusedBeforeAnyLabelChangesAsync(bool removing, string json)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(json) : Unexpected(index)
        );

        PullRequestTargetRefusedException exception = await Assert.ThrowsAsync<PullRequestTargetRefusedException>(() =>
            this.ChangeAsync(upstream: upstream, removing: removing, labels: ["Bug"])
        );

        Assert.Equal(
            expected: "credfeto/credfeto-workflow-mcp#5 is a pull request, not an issue",
            actual: exception.Message
        );
        RecordedRequest request = Assert.Single(upstream.Requests);
        AssertRequest(request: request, method: HttpMethod.Get, uri: ISSUE_URI);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingIssueIsNotFoundAsync(bool removing)
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        IssueNotFoundException exception = await Assert.ThrowsAsync<IssueNotFoundException>(() =>
            this.ChangeAsync(upstream: upstream, removing: removing, labels: ["Bug"])
        );

        Assert.Equal(
            expected: "the issue credfeto/credfeto-workflow-mcp#5 was not found, or the caller cannot see it",
            actual: exception.Message
        );
        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedIssueCheckChangesNothingAsync(bool removing)
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        LabelChangeFailedException exception = await Assert.ThrowsAsync<LabelChangeFailedException>(() =>
            this.ChangeAsync(upstream: upstream, removing: removing, labels: ["Bug"])
        );

        Assert.Equal(expected: CHECK_FAILED, actual: exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidIssueCheckResponseChangesNothingAsync(bool removing)
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json("null"));

        LabelChangeFailedException exception = await Assert.ThrowsAsync<LabelChangeFailedException>(() =>
            this.ChangeAsync(upstream: upstream, removing: removing, labels: ["Bug"])
        );

        Assert.Equal(expected: CHECK_FAILED, actual: exception.Message);
        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData(false, null, "must be given")]
    [InlineData(true, null, "must be given")]
    [InlineData(false, 0, "must be above 0")]
    [InlineData(true, -1, "must be above 0")]
    public async Task InvalidIssueIsRejectedWithoutCallingGitHubAsync(bool removing, int? issue, string rule)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ChangeAsync(upstream: upstream, removing: removing, labels: ["Bug"], issue: issue)
        );

        Assert.Equal(expected: "issue", actual: exception.ParameterName);
        Assert.Equal(expected: rule, actual: exception.Rule);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidRepositoryIsRejectedWithoutCallingGitHubAsync(bool removing)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ChangeAsync(upstream: upstream, removing: removing, labels: ["Bug"], repository: "credfeto")
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(false, "High")]
    [InlineData(true, "High")]
    [InlineData(true, "On Hold")]
    public async Task RefusedLabelIsRefusedWithoutCallingGitHubAsync(bool removing, string label)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        LabelRefusedException exception = await Assert.ThrowsAsync<LabelRefusedException>(() =>
            this.ChangeAsync(upstream: upstream, removing: removing, labels: [label])
        );

        Assert.Equal(expected: label, actual: exception.Label);
        Assert.Empty(upstream.Requests);
    }
}
