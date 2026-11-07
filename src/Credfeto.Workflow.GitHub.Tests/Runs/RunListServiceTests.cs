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
using Credfeto.Workflow.GitHub.Runs;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Runs;

public sealed class RunListServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string SHA = "0123456789abcdef0123456789abcdef01234567";
    private const string FAILED = "could not list the workflow runs of credfeto/credfeto-workflow-mcp";
    private const string API = GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/actions/";

    private static readonly Uri WorkflowsUri = new(API + "workflows?per_page=100");

    private static string Run(
        long id,
        string branch = "main",
        string sha = SHA,
        string createdAt = "2026-01-02T03:04:05Z",
        string name = "Build"
    )
    {
        return "{\"id\":"
            + id.ToString(CultureInfo.InvariantCulture)
            + ",\"name\":\""
            + name
            + "\",\"status\":\"completed\",\"conclusion\":\"success\",\"head_sha\":\""
            + sha
            + "\",\"head_branch\":\""
            + branch
            + "\",\"event\":\"push\",\"run_attempt\":1,\"created_at\":\""
            + createdAt
            + "\"}";
    }

    private static string Runs(params string[] runs)
    {
        return "{\"total_count\":"
            + runs.Length.ToString(CultureInfo.InvariantCulture)
            + ",\"workflow_runs\":["
            + string.Join(separator: ',', value: runs)
            + "]}";
    }

    private static string Workflow(long id, string name, string path)
    {
        return "{\"id\":"
            + id.ToString(CultureInfo.InvariantCulture)
            + ",\"name\":\""
            + name
            + "\",\"path\":\""
            + path
            + "\",\"state\":\"active\"}";
    }

    private static string Workflows(params string[] workflows)
    {
        return "{\"total_count\":"
            + workflows.Length.ToString(CultureInfo.InvariantCulture)
            + ",\"workflows\":["
            + string.Join(separator: ',', value: workflows)
            + "]}";
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private async Task<RunListResponse> ListAsync(
        RecordingHttpMessageHandler upstream,
        string? branch = "main",
        string? sha = null,
        string? workflow = null,
        int limit = RunQuery.DEFAULT_LIMIT,
        string repository = REPOSITORY
    )
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return await new RunListService(rest).ListAsync(
            repository: repository,
            branch: branch,
            sha: sha,
            workflow: workflow,
            limit: limit,
            cancellationToken: this.CancellationToken()
        );
    }

    private static IReadOnlyList<long> Ids(RunListResponse response)
    {
        return [.. response.Runs.Select(run => run.Id)];
    }

    [Fact]
    public async Task ListsTheRunsOfABranchAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Runs(Run(5))));

        RunListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        RunListEntry entry = Assert.Single(response.Runs);
        Assert.Equal(
            expected: new RunListEntry(
                Id: 5,
                Workflow: "Build",
                Status: "completed",
                Conclusion: "success",
                HeadSha: SHA,
                Event: "push",
                CreatedAt: "2026-01-02T03:04:05Z"
            ),
            actual: entry
        );
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
        Assert.Equal(expected: new Uri(API + "runs?branch=main&per_page=20"), actual: request.RequestUri);
    }

    [Fact]
    public async Task BranchIsEscapedInTheQueryAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Runs(Run(id: 5, branch: "feat/43-x"))));

        RunListResponse response = await this.ListAsync(upstream: upstream, branch: "feat/43-x", limit: 3);

        Assert.Equal(expected: [5L], actual: Ids(response));
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: API + "runs?branch=feat%2F43-x&per_page=3", actual: request.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task ListsTheRunsOfACommitAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Runs(Run(id: 5, branch: "other"))));

        RunListResponse response = await this.ListAsync(upstream: upstream, branch: null, sha: SHA.ToUpperInvariant());

        Assert.Equal(expected: [5L], actual: Ids(response));
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: new Uri(API + "runs?head_sha=" + SHA + "&per_page=20"), actual: request.RequestUri);
    }

    [Theory]
    [InlineData("main", SHA)]
    [InlineData(null, null)]
    public async Task ExactlyOneOfBranchOrShaMustBeGivenAsync(string? branch, string? sha)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ListAsync(upstream: upstream, branch: branch, sha: sha)
        );

        Assert.Equal(expected: "exactly one of branch or sha must be given", actual: exception.Rule);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("credfeto")]
    [InlineData("")]
    public async Task InvalidRepositoryIsRejectedWithoutRequestAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ListAsync(upstream: upstream, repository: repository)
        );

        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task InvalidLimitIsRejectedWithoutRequestAsync(int limit)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ListAsync(upstream: upstream, limit: limit)
        );

        Assert.Equal(expected: "limit", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task InvalidShaIsRejectedWithoutRequestAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ListAsync(upstream: upstream, branch: null, sha: "abc")
        );

        Assert.Equal(expected: "sha", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task InvalidWorkflowIsRejectedWithoutRequestAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ListAsync(upstream: upstream, workflow: "-x")
        );

        Assert.Equal(expected: "workflow", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task RunsAreNewestFirstAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Json(
                Runs(
                    Run(id: 1, createdAt: "2026-01-01T00:00:00Z"),
                    Run(id: 3, createdAt: "2026-01-03T00:00:00Z"),
                    Run(id: 2, createdAt: "2026-01-03T00:00:00Z"),
                    Run(id: 4, createdAt: "2026-01-02T00:00:00Z")
                )
            )
        );

        RunListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: [3L, 2L, 4L, 1L], actual: Ids(response));
    }

    [Fact]
    public async Task NoRunsIsEmptyAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Runs()));

        RunListResponse response = await this.ListAsync(upstream);

        Assert.Empty(response.Runs);
    }

    [Fact]
    public async Task RunsAreCutToTheLimitAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Json(
                Runs(
                    Run(id: 1, createdAt: "2026-01-01T00:00:00Z"),
                    Run(id: 2, createdAt: "2026-01-02T00:00:00Z"),
                    Run(id: 3, createdAt: "2026-01-03T00:00:00Z")
                )
            )
        );

        RunListResponse response = await this.ListAsync(upstream: upstream, limit: 2);

        Assert.Equal(expected: [3L, 2L], actual: Ids(response));
    }

    [Theory]
    [InlineData("other", SHA)]
    [InlineData("Main", SHA)]
    public async Task RunOfAnotherBranchIsAFailureAsync(string branch, string sha)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Json(Runs(Run(1), Run(id: 2, branch: branch, sha: sha)))
        );

        RunListFailedException exception = await Assert.ThrowsAsync<RunListFailedException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
    }

    [Fact]
    public async Task RunOfAnotherCommitIsAFailureAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Json(Runs(Run(id: 2, sha: "1123456789abcdef0123456789abcdef01234567")))
        );

        RunListFailedException exception = await Assert.ThrowsAsync<RunListFailedException>(() =>
            this.ListAsync(upstream: upstream, branch: null, sha: SHA)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
    }

    [Theory]
    [InlineData("{\"total_count\":1,\"workflow_runs\":[null]}")]
    [InlineData("{\"total_count\":1,\"workflow_runs\":[{\"head_branch\":\"main\"}]}")]
    [InlineData("{\"total_count\":0}")]
    public async Task InvalidRunsAreAFailureAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        RunListFailedException exception = await Assert.ThrowsAsync<RunListFailedException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
    }

    [Fact]
    public async Task MissingRepositoryIsAFailureAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(HttpStatusCode.NotFound));

        RunListFailedException exception = await Assert.ThrowsAsync<RunListFailedException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(
            expected: "the repository credfeto/credfeto-workflow-mcp was not found, or the caller cannot see it",
            actual: exception.Message
        );
    }

    [Fact]
    public async Task UpstreamFailureIsAFailureAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(HttpStatusCode.BadGateway));

        RunListFailedException exception = await Assert.ThrowsAsync<RunListFailedException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
    }

    [Fact]
    public async Task WorkflowIsFoundByItsFileNameAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Responses.Json(
                    Workflows(
                        Workflow(id: 10, name: "build.yml", path: ".github/workflows/other.yml"),
                        Workflow(id: 11, name: "Build", path: ".github/workflows/build.yml")
                    )
                )
                : Responses.Json(Runs(Run(5)))
        );

        RunListResponse response = await this.ListAsync(upstream: upstream, workflow: "build.yml");

        Assert.Equal(expected: [5L], actual: Ids(response));
        Assert.Equal(
            expected: [WorkflowsUri, new Uri(API + "workflows/11/runs?branch=main&per_page=20")],
            actual: [.. upstream.Requests.Select(request => request.RequestUri)]
        );
    }

    [Fact]
    public async Task WorkflowIsFoundByItsNameAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Responses.Json(
                    Workflows(
                        Workflow(id: 10, name: "Lint", path: ".github/workflows/lint.yml"),
                        Workflow(id: 11, name: "Build", path: ".github/workflows/build.yml")
                    )
                )
                : Responses.Json(Runs(Run(5)))
        );

        await this.ListAsync(upstream: upstream, workflow: "Build");

        Assert.Equal(
            expected: new Uri(API + "workflows/11/runs?branch=main&per_page=20"),
            actual: upstream.Requests[1].RequestUri
        );
    }

    [Fact]
    public async Task DisabledWorkflowIsStillFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Responses.Json(
                    "{\"total_count\":1,\"workflows\":[{\"id\":12,\"name\":\"Old\",\"path\":\".github/workflows/old.yml\",\"state\":\"disabled_manually\"}]}"
                )
                : Responses.Json(Runs(Run(5)))
        );

        await this.ListAsync(upstream: upstream, workflow: "old.yml");

        Assert.Equal(
            expected: new Uri(API + "workflows/12/runs?branch=main&per_page=20"),
            actual: upstream.Requests[1].RequestUri
        );
    }

    [Fact]
    public async Task UnknownWorkflowIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Json(Workflows(Workflow(id: 10, name: "Build", path: ".github/workflows/build.yml")))
        );

        WorkflowNotFoundException exception = await Assert.ThrowsAsync<WorkflowNotFoundException>(() =>
            this.ListAsync(upstream: upstream, workflow: "deploy.yml")
        );

        Assert.Equal(
            expected: "the workflow deploy.yml was not found in credfeto/credfeto-workflow-mcp",
            actual: exception.Message
        );
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task WorkflowNameIsMatchedExactlyAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Json(Workflows(Workflow(id: 10, name: "Build", path: ".github/workflows/build.yml")))
        );

        await Assert.ThrowsAsync<WorkflowNotFoundException>(() =>
            this.ListAsync(upstream: upstream, workflow: "build")
        );
    }

    [Fact]
    public async Task AmbiguousWorkflowNameIsAFailureAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Json(
                Workflows(
                    Workflow(id: 10, name: "Build", path: ".github/workflows/build.yml"),
                    Workflow(id: 11, name: "Build", path: ".github/workflows/build-again.yml")
                )
            )
        );

        RunListFailedException exception = await Assert.ThrowsAsync<RunListFailedException>(() =>
            this.ListAsync(upstream: upstream, workflow: "Build")
        );

        Assert.Equal(
            expected: "more than one workflow in credfeto/credfeto-workflow-mcp matches Build; give its file name",
            actual: exception.Message
        );
    }

    [Fact]
    public async Task WorkflowWithNoIdIsAFailureAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Json(
                "{\"total_count\":1,\"workflows\":[{\"name\":\"Build\",\"path\":\".github/workflows/build.yml\"}]}"
            )
        );

        RunListFailedException exception = await Assert.ThrowsAsync<RunListFailedException>(() =>
            this.ListAsync(upstream: upstream, workflow: "Build")
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
    }

    [Fact]
    public async Task WorkflowWithNoPathIsMatchedByNameOnlyAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Responses.Json("{\"total_count\":1,\"workflows\":[{\"id\":13,\"name\":\"Build\"}]}")
                : Responses.Json(Runs(Run(5)))
        );

        await this.ListAsync(upstream: upstream, workflow: "Build");

        Assert.Equal(
            expected: new Uri(API + "workflows/13/runs?branch=main&per_page=20"),
            actual: upstream.Requests[1].RequestUri
        );
    }
}
