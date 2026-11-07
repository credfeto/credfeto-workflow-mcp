using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Runs;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Runs;

public sealed class RunViewServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string FAILED = "could not read the run 5 of credfeto/credfeto-workflow-mcp";
    private const string API = GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY;

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private async Task<RunViewResponse> ViewAsync(RecordingHttpMessageHandler upstream, string run = "5")
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return await new RunViewService(rest).ViewAsync(
            repository: REPOSITORY,
            run: run,
            cancellationToken: this.CancellationToken()
        );
    }

    [Fact]
    public async Task ReadsTheRunAndTheJobsOfItsLatestAttemptAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Responses.Json(RunJson.Run(5))
                : Responses.Json(
                    RunJson.Jobs(
                        2,
                        RunJson.Job(id: 30, runId: 5, name: "test", conclusion: "success"),
                        RunJson.Job(id: 20, runId: 5, name: "build")
                    )
                )
        );

        RunViewResponse response = await this.ViewAsync(upstream);

        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        Assert.Equal(expected: 5, actual: response.Id);
        Assert.Equal(expected: "completed", actual: response.Status);
        Assert.Equal(expected: "failure", actual: response.Conclusion);
        Assert.Equal(expected: RunJson.SHA, actual: response.HeadSha);
        Assert.Equal(expected: "main", actual: response.HeadBranch);
        Assert.Equal(expected: "Build", actual: response.Workflow);
        Assert.Equal(expected: "push", actual: response.Event);
        Assert.Equal(expected: 2, actual: response.Attempt);
        Assert.Equal(expected: "2026-01-02T02:04:05Z", actual: response.CreatedAt);
        Assert.Equal(
            expected:
            [
                new RunJobEntry(Id: 20, Name: "build", Status: "completed", Conclusion: "failure"),
                new RunJobEntry(Id: 30, Name: "test", Status: "completed", Conclusion: "success"),
            ],
            actual: response.Jobs
        );
        Assert.Equal(
            expected: [new Uri(API + "/actions/runs/5"), new Uri(API + "/actions/runs/5/attempts/2/jobs?per_page=100")],
            actual: [.. upstream.Requests.Select(request => request.RequestUri)]
        );
    }

    [Fact]
    public async Task MissingFieldsAreShownAsNoneAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Responses.Json("{\"id\":5,\"run_attempt\":1}")
                : Responses.Json("{\"total_count\":1,\"jobs\":[{\"id\":20}]}")
        );

        RunViewResponse response = await this.ViewAsync(upstream);

        Assert.Equal(expected: "-", actual: response.Status);
        Assert.Equal(expected: "-", actual: response.Conclusion);
        Assert.Equal(expected: "-", actual: response.CreatedAt);
        Assert.Equal(
            expected: [new RunJobEntry(Id: 20, Name: "-", Status: "-", Conclusion: "-")],
            actual: response.Jobs
        );
    }

    [Fact]
    public async Task LeadingZerosDoNotChangeTheRunAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(RunJson.Run(5)) : Responses.Json(RunJson.Jobs(0))
        );

        RunViewResponse response = await this.ViewAsync(upstream: upstream, run: "005");

        Assert.Equal(expected: 5, actual: response.Id);
        Assert.Empty(response.Jobs);
        Assert.Equal(expected: new Uri(API + "/actions/runs/5"), actual: upstream.Requests[0].RequestUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("5/jobs")]
    public async Task InvalidRunIsRejectedWithoutRequestAsync(string run)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ViewAsync(upstream: upstream, run: run)
        );

        Assert.Equal(expected: "run", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task MissingRunIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Responses.Status(HttpStatusCode.NotFound)
                : Responses.Json("{\"full_name\":\"credfeto/credfeto-workflow-mcp\",\"default_branch\":\"main\"}")
        );

        RunNotFoundException exception = await Assert.ThrowsAsync<RunNotFoundException>(() => this.ViewAsync(upstream));

        Assert.Equal(expected: "the run 5 was not found in credfeto/credfeto-workflow-mcp", actual: exception.Message);
        Assert.Equal(expected: new Uri(API), actual: upstream.Requests[1].RequestUri);
    }

    [Fact]
    public async Task MissingRepositoryIsAFailureAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(HttpStatusCode.NotFound));

        RunViewFailedException exception = await Assert.ThrowsAsync<RunViewFailedException>(() =>
            this.ViewAsync(upstream)
        );

        Assert.Equal(
            expected: "the repository credfeto/credfeto-workflow-mcp was not found, or the caller cannot see it",
            actual: exception.Message
        );
    }

    [Fact]
    public async Task RepositoryReadFailureIsAFailureAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Status(HttpStatusCode.NotFound) : Responses.Status(HttpStatusCode.BadGateway)
        );

        RunViewFailedException exception = await Assert.ThrowsAsync<RunViewFailedException>(() =>
            this.ViewAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
    }

    [Theory]
    [InlineData("{\"id\":6,\"run_attempt\":1}")]
    [InlineData("{\"run_attempt\":1}")]
    [InlineData("{\"id\":5}")]
    [InlineData("{\"id\":5,\"run_attempt\":0}")]
    public async Task InvalidRunIsAFailureAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        RunViewFailedException exception = await Assert.ThrowsAsync<RunViewFailedException>(() =>
            this.ViewAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("{\"total_count\":2,\"jobs\":[{\"id\":20}]}")]
    [InlineData("{\"jobs\":[{\"id\":20}]}")]
    [InlineData("{\"total_count\":1,\"jobs\":[{\"name\":\"build\"}]}")]
    [InlineData("{\"total_count\":1,\"jobs\":[null]}")]
    [InlineData("{\"total_count\":1001,\"jobs\":[]}")]
    public async Task InvalidJobsAreAFailureAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(RunJson.Run(5)) : Responses.Json(json)
        );

        RunViewFailedException exception = await Assert.ThrowsAsync<RunViewFailedException>(() =>
            this.ViewAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
    }

    [Fact]
    public async Task TotalThatChangesBetweenPagesIsAFailureAsync()
    {
        const string next = "<" + API + "/actions/runs/5/attempts/2/jobs?per_page=100&page=2>; rel=\"next\"";

        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(RunJson.Run(5)),
                1 => Responses.Json(json: RunJson.Jobs(2, RunJson.Job(id: 20, runId: 5)), nextLink: next),
                _ => Responses.Json(RunJson.Jobs(3, RunJson.Job(id: 21, runId: 5))),
            }
        );

        RunViewFailedException exception = await Assert.ThrowsAsync<RunViewFailedException>(() =>
            this.ViewAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task JobsAcrossPagesAreReadAsync()
    {
        const string next = "<" + API + "/actions/runs/5/attempts/2/jobs?per_page=100&page=2>; rel=\"next\"";

        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(RunJson.Run(5)),
                1 => Responses.Json(json: RunJson.Jobs(2, RunJson.Job(id: 21, runId: 5)), nextLink: next),
                _ => Responses.Json(RunJson.Jobs(2, RunJson.Job(id: 20, runId: 5))),
            }
        );

        RunViewResponse response = await this.ViewAsync(upstream);

        Assert.Equal(expected: [20L, 21L], actual: [.. response.Jobs.Select(job => job.Id)]);
    }
}
