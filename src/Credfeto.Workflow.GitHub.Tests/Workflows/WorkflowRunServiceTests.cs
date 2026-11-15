using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Credfeto.Workflow.GitHub.Workflows;
using NSubstitute;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Workflows;

public sealed class WorkflowRunServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string WORKFLOW = "deploy.yml";
    private const string DEFAULT_BRANCH = "develop";
    private const string RAW = "application/vnd.github.raw";

    private const string REPOSITORY_JSON =
        "{\"full_name\":\"credfeto/credfeto-workflow-mcp\",\"default_branch\":\"develop\"}";

    private const string WORKFLOW_JSON =
        "{\"id\":1,\"name\":\"Deploy\",\"path\":\".github/workflows/deploy.yml\",\"state\":\"active\"}";

    private const string DEFINITION = """
        name: Deploy
        on:
          workflow_dispatch:
            inputs:
              environment:
                type: string
              dry-run:
                type: boolean
        jobs:
          deploy:
            runs-on: ubuntu-latest
        """;

    private const string RUNS_JSON =
        "{\"total_count\":1,\"workflow_runs\":[{\"id\":7,\"html_url\":\"https://github.com/credfeto/credfeto-workflow-mcp/actions/runs/7\"}]}";

    private const string BASE = GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY;

    private readonly ILeakChecker _leakChecker;

    public WorkflowRunServiceTests()
    {
        this._leakChecker = GetSubstitute<ILeakChecker>();
        this._leakChecker.CheckRepositoryReferencesAsync(
                repository: Arg.Any<string>(),
                subject: Arg.Any<LeakSubject>(),
                text: Arg.Any<string>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns((LeakRefusal?)null);
    }

    private static RecordingHttpMessageHandler Respond(params Func<HttpResponseMessage>[] responses)
    {
        return new(index =>
            index < responses.Length
                ? responses[index]()
                : throw new InvalidOperationException(
                    "Unexpected request " + index.ToString(CultureInfo.InvariantCulture)
                )
        );
    }

    private static Func<HttpResponseMessage> Json(string json)
    {
        return () => Responses.Json(json);
    }

    private static Func<HttpResponseMessage> Text(string text)
    {
        return () => Responses.WithContentType(body: text, contentType: RAW);
    }

    private static Func<HttpResponseMessage> Status(HttpStatusCode statusCode)
    {
        return () => Responses.Status(statusCode);
    }

    // The requests every call that reaches the dispatch makes first, in order
    private static Func<HttpResponseMessage>[] UpToDispatch(
        string allowList = "deploy.yml\n",
        string definition = DEFINITION
    )
    {
        return [Json(REPOSITORY_JSON), Text(allowList), Json(WORKFLOW_JSON), Text(definition)];
    }

    private WorkflowRunService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(restClient: rest, leakChecker: this._leakChecker);
    }

    private async Task<WorkflowRunResult> RunAsync(
        RecordingHttpMessageHandler upstream,
        string? gitRef = null,
        IReadOnlyList<string>? inputs = null,
        string repository = REPOSITORY,
        string workflow = WORKFLOW
    )
    {
        return await this.CreateService(upstream)
            .RunAsync(
                repository: repository,
                workflow: workflow,
                gitRef: gitRef,
                inputs: inputs,
                cancellationToken: this.CancellationToken()
            );
    }

    private static void AssertRefused(WorkflowRunResult result, string rule, string gitRef = DEFAULT_BRANCH)
    {
        Assert.Equal(expected: WorkflowRunOutcome.Refused, actual: result.Outcome);
        Assert.Equal(expected: "workflow run refused: " + rule, actual: result.Message);
        Assert.Equal(expected: rule, actual: result.Rule);
        Assert.Equal(expected: WORKFLOW, actual: result.Workflow);
        Assert.Equal(expected: gitRef, actual: result.Ref);
        Assert.Null(result.Run);
    }

    private static void AssertNoDispatch(RecordingHttpMessageHandler upstream)
    {
        Assert.DoesNotContain(upstream.Requests, request => request.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData("not-a-repository", WORKFLOW, null, "repository")]
    [InlineData(REPOSITORY, "deploy.txt", null, "workflow")]
    [InlineData(REPOSITORY, ".github/workflows/deploy.yml", null, "workflow")]
    [InlineData(REPOSITORY, WORKFLOW, "bad..ref", "gitRef")]
    public async Task InvalidParametersAreRefusedWithoutCallingGitHubAsync(
        string repository,
        string workflow,
        string? gitRef,
        string parameterName
    )
    {
        using RecordingHttpMessageHandler upstream = Respond();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.RunAsync(upstream: upstream, gitRef: gitRef, repository: repository, workflow: workflow)
        );

        Assert.Equal(expected: parameterName, actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("environment", "each input must be given as key=value")]
    [InlineData("=staging", "each input must be given as key=value")]
    [InlineData("bad key=staging", "an input key is not a valid workflow input name")]
    public async Task AnInputInTheWrongFormIsInvalidWithoutCallingGitHubAsync(string input, string message)
    {
        using RecordingHttpMessageHandler upstream = Respond();

        WorkflowRunResult result = await this.RunAsync(upstream: upstream, gitRef: "main", inputs: [input]);

        Assert.Equal(expected: WorkflowRunOutcome.InvalidInput, actual: result.Outcome);
        Assert.Equal(expected: message, actual: result.Message);
        Assert.Equal(expected: "main", actual: result.Ref);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task AnInputGivenTwiceIsInvalidAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond();

        WorkflowRunResult result = await this.RunAsync(
            upstream: upstream,
            inputs: ["environment=staging", "environment=production"]
        );

        Assert.Equal(expected: WorkflowRunOutcome.InvalidInput, actual: result.Outcome);
        Assert.Equal(expected: "the input environment is given more than once", actual: result.Message);
        Assert.Null(result.Ref);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task AnInputThatIsTooLongIsInvalidAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond();

        WorkflowRunResult result = await this.RunAsync(
            upstream: upstream,
            inputs: ["environment=" + new string(c: 'a', count: 65536)]
        );

        Assert.Equal(expected: WorkflowRunOutcome.InvalidInput, actual: result.Outcome);
        Assert.Equal(expected: "the input environment is too long", actual: result.Message);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task AMissingRepositoryFailsAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(Status(HttpStatusCode.NotFound));

        WorkflowRunFailedException exception = await Assert.ThrowsAsync<WorkflowRunFailedException>(() =>
            this.RunAsync(upstream)
        );

        Assert.Equal(
            expected: "the repository credfeto/credfeto-workflow-mcp was not found, or the caller cannot see it",
            actual: exception.Message
        );
        Assert.IsType<RepositoryNotFoundException>(exception.InnerException);
    }

    [Fact]
    public async Task ARepositoryWithoutADefaultBranchFailsAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            Json("{\"full_name\":\"credfeto/credfeto-workflow-mcp\"}")
        );

        WorkflowRunFailedException exception = await Assert.ThrowsAsync<WorkflowRunFailedException>(() =>
            this.RunAsync(upstream)
        );

        Assert.Equal(
            expected: "could not run the workflow deploy.yml of credfeto/credfeto-workflow-mcp",
            actual: exception.Message
        );
        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task AnUpstreamFailureReadingTheAllowListFailsAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            Json(REPOSITORY_JSON),
            Status(HttpStatusCode.InternalServerError)
        );

        WorkflowRunFailedException exception = await Assert.ThrowsAsync<WorkflowRunFailedException>(() =>
            this.RunAsync(upstream)
        );

        Assert.Equal(
            expected: "could not run the workflow deploy.yml of credfeto/credfeto-workflow-mcp",
            actual: exception.Message
        );
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task NoAllowListRefusesEveryWorkflowAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(Json(REPOSITORY_JSON), Status(HttpStatusCode.NotFound));

        WorkflowRunResult result = await this.RunAsync(upstream);

        AssertRefused(result: result, rule: "the repository has no .github/cfwf-dispatch-allow on its default branch");
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        RecordedRequest allowList = upstream.Requests[1];
        Assert.Equal(
            expected: new Uri(BASE + "/contents/.github/cfwf-dispatch-allow?ref=develop"),
            actual: allowList.RequestUri
        );
        Assert.Equal(expected: "application/vnd.github.raw+json", actual: allowList.Accept);
    }

    [Fact]
    public async Task AWorkflowThatIsNotListedIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(Json(REPOSITORY_JSON), Text("build.yml *\n"));

        WorkflowRunResult result = await this.RunAsync(upstream);

        AssertRefused(result: result, rule: "the workflow is not listed in .github/cfwf-dispatch-allow");
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task ARefThatIsNotPermittedIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(Json(REPOSITORY_JSON), Text("deploy.yml release/1\n"));

        WorkflowRunResult result = await this.RunAsync(upstream: upstream, gitRef: "feature/x");

        AssertRefused(
            result: result,
            rule: "the ref is not permitted for the workflow by .github/cfwf-dispatch-allow",
            gitRef: "feature/x"
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData("deploy.yml\n", null, DEFAULT_BRANCH)]
    [InlineData("deploy.yml\n", DEFAULT_BRANCH, DEFAULT_BRANCH)]
    [InlineData("deploy.yml release/1\n", "release/1", "release/1")]
    [InlineData("deploy.yml *\n", "feature/x", "feature/x")]
    public async Task APermittedRefIsDispatchedAsync(string allowList, string? gitRef, string expectedRef)
    {
        using RecordingHttpMessageHandler upstream = Respond([
            .. UpToDispatch(allowList: allowList),
            Status(HttpStatusCode.NoContent),
            Json(RUNS_JSON),
        ]);

        WorkflowRunResult result = await this.RunAsync(upstream: upstream, gitRef: gitRef);

        Assert.Equal(expected: WorkflowRunOutcome.Success, actual: result.Outcome);
        Assert.Equal(expected: expectedRef, actual: result.Ref);
        string escaped = Uri.EscapeDataString(expectedRef);
        Assert.Equal(
            expected: new Uri(BASE + "/contents/.github/workflows/deploy.yml?ref=" + escaped),
            actual: upstream.Requests[3].RequestUri
        );
        Assert.Equal(
            expected: new Uri(
                BASE + "/actions/workflows/deploy.yml/runs?event=workflow_dispatch&branch=" + escaped + "&per_page=1"
            ),
            actual: upstream.Requests[5].RequestUri
        );
    }

    [Fact]
    public async Task AWorkflowGitHubDoesNotKnowIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            Json(REPOSITORY_JSON),
            Text("deploy.yml\n"),
            Status(HttpStatusCode.NotFound)
        );

        WorkflowRunResult result = await this.RunAsync(upstream);

        Assert.Equal(expected: WorkflowRunOutcome.NotFound, actual: result.Outcome);
        Assert.Equal(
            expected: "the workflow deploy.yml was not found in credfeto/credfeto-workflow-mcp",
            actual: result.Message
        );
        Assert.Equal(
            expected: new Uri(BASE + "/actions/workflows/deploy.yml"),
            actual: upstream.Requests[2].RequestUri
        );
        AssertNoDispatch(upstream);
    }

    [Fact]
    public async Task AWorkflowFileMissingOnTheRefIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            Json(REPOSITORY_JSON),
            Text("deploy.yml *\n"),
            Json(WORKFLOW_JSON),
            Status(HttpStatusCode.NotFound)
        );

        WorkflowRunResult result = await this.RunAsync(upstream: upstream, gitRef: "release/1");

        Assert.Equal(expected: WorkflowRunOutcome.NotFound, actual: result.Outcome);
        Assert.Equal(expected: "the workflow deploy.yml was not found on release/1", actual: result.Message);
        Assert.Equal(expected: "release/1", actual: result.Ref);
        AssertNoDispatch(upstream);
    }

    [Fact]
    public async Task AWorkflowWithoutWorkflowDispatchIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(UpToDispatch(definition: "on: push\n"));

        WorkflowRunResult result = await this.RunAsync(upstream);

        AssertRefused(result: result, rule: "the workflow does not declare workflow_dispatch");
        Assert.Equal(expected: 4, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task AnInputTheWorkflowDoesNotDeclareIsInvalidAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(UpToDispatch());

        WorkflowRunResult result = await this.RunAsync(
            upstream: upstream,
            inputs: ["environment=staging", "zone=eu", "region=eu"]
        );

        Assert.Equal(expected: WorkflowRunOutcome.InvalidInput, actual: result.Outcome);
        Assert.Equal(expected: "the workflow does not declare the input region", actual: result.Message);
        Assert.Equal(expected: DEFAULT_BRANCH, actual: result.Ref);
        AssertNoDispatch(upstream);
    }

    [Fact]
    public async Task AnInputValueThatLeaksIsRefusedAsync()
    {
        this._leakChecker.CheckValue(subject: LeakSubject.TEXT, value: "ghp_secret")
            .Returns(new LeakRefusal(Subject: LeakSubject.TEXT, Rule: LeakRule.TOKEN, LineNumber: 1));
        using RecordingHttpMessageHandler upstream = Respond(UpToDispatch());

        WorkflowRunResult result = await this.RunAsync(upstream: upstream, inputs: ["environment=ghp_secret"]);

        Assert.Equal(expected: WorkflowRunOutcome.Refused, actual: result.Outcome);
        Assert.Equal(expected: "workflow run refused: input environment token", actual: result.Message);
        Assert.Equal(expected: "token", actual: result.Rule);
        AssertNoDispatch(upstream);
    }

    [Fact]
    public async Task AnInputValueNamingAPrivateRepositoryIsRefusedAsync()
    {
        this._leakChecker.CheckRepositoryReferencesAsync(
                repository: REPOSITORY,
                subject: LeakSubject.TEXT,
                text: "private/repo",
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new LeakRefusal(Subject: LeakSubject.TEXT, Rule: LeakRule.PRIVATE_REPOSITORY, LineNumber: 1));
        using RecordingHttpMessageHandler upstream = Respond(UpToDispatch());

        WorkflowRunResult result = await this.RunAsync(
            upstream: upstream,
            inputs: ["dry-run=true", "environment=private/repo"]
        );

        Assert.Equal(expected: WorkflowRunOutcome.Refused, actual: result.Outcome);
        Assert.Equal(expected: "workflow run refused: input environment private repository", actual: result.Message);
        Assert.Equal(expected: "private repository", actual: result.Rule);
        AssertNoDispatch(upstream);
    }

    [Fact]
    public async Task ADispatchedRunIsReportedAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond([
            .. UpToDispatch(),
            Status(HttpStatusCode.NoContent),
            Json(RUNS_JSON),
        ]);

        WorkflowRunResult result = await this.RunAsync(
            upstream: upstream,
            inputs: ["environment=staging", "dry-run=true"]
        );

        Assert.Equal(
            expected: new WorkflowRunResult(
                Outcome: WorkflowRunOutcome.Success,
                Message: "dispatched; newest run on develop is 7",
                Workflow: WORKFLOW,
                Ref: DEFAULT_BRANCH,
                Run: 7,
                Url: new("https://github.com/credfeto/credfeto-workflow-mcp/actions/runs/7"),
                Rule: null
            ),
            actual: result
        );
        Assert.Equal(expected: 6, actual: upstream.Requests.Count);
        RecordedRequest dispatch = upstream.Requests[4];
        Assert.Equal(expected: HttpMethod.Post, actual: dispatch.Method);
        Assert.Equal(expected: new Uri(BASE + "/actions/workflows/deploy.yml/dispatches"), actual: dispatch.RequestUri);
        Assert.Equal(
            expected: "{\"ref\":\"develop\",\"inputs\":{\"environment\":\"staging\",\"dry-run\":\"true\"}}",
            actual: dispatch.Body
        );
    }

    [Fact]
    public async Task ADispatchWithoutInputsSendsAnEmptyInputsObjectAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond([
            .. UpToDispatch(),
            Status(HttpStatusCode.NoContent),
            Json(RUNS_JSON),
        ]);

        WorkflowRunResult result = await this.RunAsync(upstream);

        Assert.Equal(expected: WorkflowRunOutcome.Success, actual: result.Outcome);
        Assert.Equal(expected: "{\"ref\":\"develop\",\"inputs\":{}}", actual: upstream.Requests[4].Body);
    }

    [Theory]
    [InlineData("{\"total_count\":0,\"workflow_runs\":[]}")]
    [InlineData("{\"total_count\":1,\"workflow_runs\":[null]}")]
    [InlineData("{\"total_count\":1,\"workflow_runs\":[{\"html_url\":\"https://github.com/x\"}]}")]
    [InlineData("{\"total_count\":0}")]
    public async Task ARunThatIsNotVisibleYetIsStillASuccessAsync(string runs)
    {
        using RecordingHttpMessageHandler upstream = Respond([
            .. UpToDispatch(),
            Status(HttpStatusCode.NoContent),
            Json(runs),
        ]);

        WorkflowRunResult result = await this.RunAsync(upstream);

        AssertNotVisible(result);
    }

    [Fact]
    public async Task AFailureReadingTheRunBackIsStillASuccessAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond([
            .. UpToDispatch(),
            Status(HttpStatusCode.NoContent),
            Status(HttpStatusCode.InternalServerError),
        ]);

        WorkflowRunResult result = await this.RunAsync(upstream);

        AssertNotVisible(result);
    }

    [Theory]
    [InlineData("http://github.com/credfeto/credfeto-workflow-mcp/actions/runs/7")]
    [InlineData("/credfeto/credfeto-workflow-mcp/actions/runs/7")]
    public async Task ARunUrlThatIsNotHttpsIsDroppedAsync(string url)
    {
        using RecordingHttpMessageHandler upstream = Respond([
            .. UpToDispatch(),
            Status(HttpStatusCode.NoContent),
            Json("{\"total_count\":1,\"workflow_runs\":[{\"id\":7,\"html_url\":\"" + url + "\"}]}"),
        ]);

        WorkflowRunResult result = await this.RunAsync(upstream);

        Assert.Equal(expected: WorkflowRunOutcome.Success, actual: result.Outcome);
        Assert.Equal(expected: 7, actual: result.Run);
        Assert.Null(result.Url);
    }

    [Fact]
    public async Task ADispatchGitHubCannotProcessIsInvalidInputAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond([
            .. UpToDispatch(),
            () =>
                Responses.StatusWithJson(
                    statusCode: HttpStatusCode.UnprocessableEntity,
                    json: "{\"message\":\"Unexpected inputs provided\"}"
                ),
        ]);

        WorkflowRunResult result = await this.RunAsync(upstream: upstream, inputs: ["environment=staging"]);

        Assert.Equal(expected: WorkflowRunOutcome.InvalidInput, actual: result.Outcome);
        Assert.Equal(
            expected: "GitHub refused to run the workflow: GitHub answered with status 422",
            actual: result.Message
        );
        Assert.Equal(expected: 5, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task ADispatchGitHubRefusesFailsAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond([.. UpToDispatch(), Status(HttpStatusCode.Forbidden)]);

        WorkflowRunFailedException exception = await Assert.ThrowsAsync<WorkflowRunFailedException>(() =>
            this.RunAsync(upstream)
        );

        Assert.Equal(
            expected: "GitHub refused to run the workflow deploy.yml of credfeto/credfeto-workflow-mcp: GitHub answered with status 403",
            actual: exception.Message
        );
        Assert.Equal(expected: 5, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task ADispatchThatMayHaveReachedGitHubFailsWithoutRetryingAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond([
            .. UpToDispatch(),
            Status(HttpStatusCode.InternalServerError),
        ]);

        WorkflowRunFailedException exception = await Assert.ThrowsAsync<WorkflowRunFailedException>(() =>
            this.RunAsync(upstream)
        );

        Assert.Equal(
            expected: "the request to run the workflow deploy.yml of credfeto/credfeto-workflow-mcp failed, and may still have reached GitHub",
            actual: exception.Message
        );
        Assert.Equal(expected: 5, actual: upstream.Requests.Count);
    }

    private static void AssertNotVisible(WorkflowRunResult result)
    {
        Assert.Equal(
            expected: new WorkflowRunResult(
                Outcome: WorkflowRunOutcome.Success,
                Message: "dispatched; the run is not visible yet",
                Workflow: WORKFLOW,
                Ref: DEFAULT_BRANCH,
                Run: null,
                Url: null,
                Rule: null
            ),
            actual: result
        );
    }
}
