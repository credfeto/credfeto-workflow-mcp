using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Checks;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Checks;

public sealed class CommitCheckRunsServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string SHA = "0123456789abcdef0123456789abcdef01234567";
    private const long APP_ID = 15368;
    private const string FIRST_PAGE =
        "repos/" + REPOSITORY + "/commits/" + SHA + "/check-runs?per_page=100&filter=latest";

    [Fact]
    public async Task ReadAsyncListsTheLatestCheckRunsOfTheCommitAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(() =>
            Responses.Json(Runs(Run(id: 1, name: "build", status: "completed", conclusion: "success")))
        );

        CommitCheckRunsResponse response = await this.ReadAsync(upstream: upstream, sha: SHA);

        Assert.Equal(expected: REPOSITORY, actual: response.Repo);
        Assert.Equal(expected: SHA, actual: response.Sha);
        Assert.Equal(expected: 0, actual: response.Running);
        Assert.Equal(
            expected: new CommitCheckRun(
                Id: 1,
                Name: "build",
                Status: "completed",
                Conclusion: "success",
                AppId: APP_ID
            ),
            actual: Assert.Single(response.CheckRuns)
        );
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: new Uri(GitHubServices.ApiBaseUrl + FIRST_PAGE), actual: request.RequestUri);
    }

    [Fact]
    public async Task ReadAsyncAsksForTheCommitInLowerCaseAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(() => Responses.Json(Runs()));

        CommitCheckRunsResponse response = await this.ReadAsync(upstream: upstream, sha: SHA.ToUpperInvariant());

        Assert.Equal(expected: SHA, actual: response.Sha);
        Assert.Empty(response.CheckRuns);
        Assert.Equal(expected: 0, actual: response.Running);
        Assert.Equal(
            expected: new Uri(GitHubServices.ApiBaseUrl + FIRST_PAGE),
            actual: Assert.Single(upstream.Requests).RequestUri
        );
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("in_progress")]
    [InlineData("waiting")]
    [InlineData("pending")]
    [InlineData("requested")]
    public async Task ReadAsyncCountsARunWithNoConclusionAndARunningStatusAsync(string status)
    {
        CommitCheckRunsResponse response = await this.ReadRunsAsync(
            Run(id: 1, name: "build", status: status, conclusion: null),
            Run(id: 2, name: "lint", status: "completed", conclusion: "failure")
        );

        Assert.Equal(expected: 1, actual: response.Running);
        Assert.Null(response.CheckRuns[0].Conclusion);
    }

    [Theory]
    [InlineData("completed", null)]
    [InlineData("in_progress", "\"success\"")]
    [InlineData("queued", "\"\"")]
    [InlineData("expected", null)]
    [InlineData("QUEUED", null)]
    public async Task ReadAsyncDoesNotCountARunThatHasAConclusionOrIsNotRunningAsync(string status, string? conclusion)
    {
        CommitCheckRunsResponse response = await this.ReadRunsAsync(
            $$$"""{"id":1,"name":"build","status":"{{{status}}}","conclusion":{{{conclusion ?? "null"}}},"app":{"id":{{{APP_ID}}} }}"""
        );

        Assert.Equal(expected: 0, actual: response.Running);
        Assert.Equal(expected: status, actual: Assert.Single(response.CheckRuns).Status);
    }

    [Fact]
    public async Task ReadAsyncCountsEveryRunningRunAsync()
    {
        CommitCheckRunsResponse response = await this.ReadRunsAsync(
            Run(id: 1, name: "build", status: "queued", conclusion: null),
            Run(id: 2, name: "build", status: "in_progress", conclusion: null),
            Run(id: 3, name: "lint", status: "completed", conclusion: "cancelled")
        );

        Assert.Equal(expected: 2, actual: response.Running);
        Assert.Equal(expected: [1L, 2L, 3L], actual: [.. response.CheckRuns.Select(run => run.Id)]);
    }

    [Fact]
    public async Task ReadAsyncKeepsARunWithNoAppAndFlattensItsNameAsync()
    {
        CommitCheckRunsResponse response = await this.ReadRunsAsync(
            """{"id":7,"name":"build\nlinux","status":"completed","conclusion":"neutral"}"""
        );

        Assert.Equal(
            expected: new CommitCheckRun(
                Id: 7,
                Name: "build linux",
                Status: "completed",
                Conclusion: "neutral",
                AppId: null
            ),
            actual: Assert.Single(response.CheckRuns)
        );
    }

    [Fact]
    public async Task ReadAsyncReadsEveryPageAsync()
    {
        string secondPage = GitHubServices.ApiBaseUrl + FIRST_PAGE + "&page=2";
        using RecordingHttpMessageHandler upstream = Respond(
            () =>
                Responses.Json(
                    json: Runs(Run(id: 1, name: "build", status: "completed", conclusion: "success")),
                    nextLink: "<" + secondPage + ">; rel=\"next\""
                ),
            () => Responses.Json(Runs(Run(id: 2, name: "test", status: "queued", conclusion: null)))
        );

        CommitCheckRunsResponse response = await this.ReadAsync(upstream: upstream, sha: SHA);

        Assert.Equal(expected: 2, actual: response.CheckRuns.Count);
        Assert.Equal(expected: 1, actual: response.Running);
        Assert.Equal(expected: new Uri(secondPage), actual: upstream.Requests[1].RequestUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task ReadAsyncReportsACommitThatWasNotFoundAsync(HttpStatusCode statusCode)
    {
        using RecordingHttpMessageHandler upstream = Respond(() => Responses.Status(statusCode));

        CommitNotFoundException exception = await Assert.ThrowsAsync<CommitNotFoundException>(() =>
            this.ReadAsync(upstream: upstream, sha: SHA).AsTask()
        );

        Assert.Equal(expected: "the commit " + SHA + " was not found in " + REPOSITORY, actual: exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task ReadAsyncReportsAFailedReadAsUnreadableAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(() =>
            Responses.Status(HttpStatusCode.InternalServerError)
        );

        PrChecksUnreadableException exception = await this.AssertUnreadableAsync(upstream);

        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"name":"build","status":"completed","conclusion":"success"}""")]
    [InlineData("""{"id":1,"status":"completed","conclusion":"success"}""")]
    [InlineData("""{"id":1,"name":"","status":"completed","conclusion":"success"}""")]
    [InlineData("""{"id":1,"name":"build","conclusion":"success"}""")]
    public async Task ReadAsyncReportsAnIncompleteRunAsUnreadableAsync(string run)
    {
        using RecordingHttpMessageHandler upstream = Respond(() => Responses.Json(Runs(run)));

        PrChecksUnreadableException exception = await this.AssertUnreadableAsync(upstream);

        GitHubInvalidResponseException inner = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: "GitHub returned a check run without an id, name or status", actual: inner.Message);
    }

    [Theory]
    [InlineData("not-a-repository", SHA, "repository")]
    [InlineData(REPOSITORY, "not-a-sha", "sha")]
    [InlineData(REPOSITORY, "0123456789abcdef0123456789abcdef0123456", "sha")]
    public async Task ReadAsyncRejectsAnInvalidRepositoryOrShaAsync(string repository, string sha, string parameter)
    {
        using RecordingHttpMessageHandler upstream = Respond();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateService(upstream)
                .ReadAsync(repository: repository, sha: sha, cancellationToken: this.CancellationToken())
                .AsTask()
        );

        Assert.Equal(expected: parameter, actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    private async Task<PrChecksUnreadableException> AssertUnreadableAsync(RecordingHttpMessageHandler upstream)
    {
        PrChecksUnreadableException exception = await Assert.ThrowsAsync<PrChecksUnreadableException>(() =>
            this.ReadAsync(upstream: upstream, sha: SHA).AsTask()
        );

        Assert.Equal(
            expected: "could not read the check runs of " + REPOSITORY + " commit " + SHA,
            actual: exception.Message
        );

        return exception;
    }

    private async ValueTask<CommitCheckRunsResponse> ReadRunsAsync(params string[] runs)
    {
        using RecordingHttpMessageHandler upstream = Respond(() => Responses.Json(Runs(runs)));

        return await this.ReadAsync(upstream: upstream, sha: SHA);
    }

    private ValueTask<CommitCheckRunsResponse> ReadAsync(RecordingHttpMessageHandler upstream, string sha)
    {
        return this.CreateService(upstream)
            .ReadAsync(repository: REPOSITORY, sha: sha, cancellationToken: this.CancellationToken());
    }

    private CommitCheckRunsService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient client = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(client);
    }

    private static RecordingHttpMessageHandler Respond(params Func<HttpResponseMessage>[] responses)
    {
        return new(index => responses[index]());
    }

    private static string Runs(params string[] runs)
    {
        return "{\"check_runs\":[" + string.Join(separator: ',', value: runs) + "]}";
    }

    private static string Run(long id, string name, string status, string? conclusion)
    {
        string value = conclusion is null ? "null" : "\"" + conclusion + "\"";

        return $$$"""{"id":{{{id}}},"name":"{{{name}}}","status":"{{{status}}}","conclusion":{{{value}}},"app":{"id":{{{APP_ID}}} }}""";
    }
}
