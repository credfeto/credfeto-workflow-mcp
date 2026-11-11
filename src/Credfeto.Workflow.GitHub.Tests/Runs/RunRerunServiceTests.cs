using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Credfeto.Workflow.GitHub.Runs;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Runs;

public sealed class RunRerunServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string API = GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY;
    private const string SHA = RunJson.SHA;
    private const string OTHER_SHA = "fedcba9876543210fedcba9876543210fedcba98";
    private const string BRANCH = "feature";
    private const string DEFAULT_BRANCH = "develop";
    private const long APP_ID = 15368;
    private const string FAILED = "could not rerun the run 5 of " + REPOSITORY;
    private const string REQUEST_FAILED =
        "the rerun request for the run 5 of " + REPOSITORY + " failed, and may still have reached GitHub";
    private const string PULLS = API + "/pulls?state=open&per_page=100";
    private const string JOBS = API + "/actions/runs/5/attempts/2/jobs?per_page=100";
    private const string NO_STATUSES = "{\"statuses\":[]}";
    private const string RERUN_BODY = "{\"enable_debug_logging\":false}";

    private static readonly IReadOnlyList<RequiredCheck> NothingRequired = [];

    private readonly IRequiredChecksReader _reader;

    public RunRerunServiceTests()
    {
        this._reader = GetSubstitute<IRequiredChecksReader>();
        this.Requires(NothingRequired);
    }

    [Fact]
    public async Task InvalidRepositoryIsRejectedWithoutRequestAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.RerunAsync(upstream: upstream, repository: "not-a-repository")
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("5/jobs")]
    public async Task InvalidRunIsRejectedWithoutRequestAsync(string run)
    {
        using RecordingHttpMessageHandler upstream = Respond();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.RerunAsync(upstream: upstream, run: run)
        );

        Assert.Equal(expected: "run", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("failure", 1)]
    [InlineData("failure", 2)]
    [InlineData("cancelled", 2)]
    [InlineData("timed_out", 1)]
    [InlineData("action_required", 1)]
    public async Task AFailedRunHasItsFailedJobsRerunAsync(string conclusion, int attempt)
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(Run(conclusion: conclusion, attempt: attempt)),
            static () => Responses.Json(Repository()),
            static () => Responses.Json(PullRequests(PullRequest(80))),
            static () => Rerunning()
        );

        RunRerunResult result = await this.RerunAsync(upstream);

        int next = attempt + 1;
        Assert.Equal(
            expected: new RunRerunResult(
                Outcome: RunRerunOutcome.Success,
                Message: "Rerunning failed jobs of run 5 (attempt " + Number(next) + ")",
                Run: 5,
                Attempt: next,
                Rule: null
            ),
            actual: result
        );
        Assert.Equal(
            expected:
            [
                new Uri(API + "/actions/runs/5"),
                new Uri(API),
                new Uri(PULLS),
                new Uri(API + "/actions/runs/5/rerun-failed-jobs"),
            ],
            actual: [.. upstream.Requests.Select(static request => request.RequestUri)]
        );
        AssertRerunPosted(upstream);
        await this.AssertRequiredChecksNotReadAsync();
    }

    [Theory]
    [InlineData(3, "completed")]
    [InlineData(4, "completed")]
    [InlineData(3, "in_progress")]
    public async Task ARunAtTheAttemptLimitIsRefusedAsync(int attempt, string status)
    {
        using RecordingHttpMessageHandler upstream = Respond(() =>
            Responses.Json(Run(status: status, attempt: attempt))
        );

        RunRerunResult result = await this.RerunAsync(upstream);

        AssertRefused(result: result, rule: "attempt limit");
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("queued")]
    public async Task ARunOnTheDefaultBranchIsRefusedAsync(string status)
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(Run(branch: DEFAULT_BRANCH, status: status)),
            static () => Responses.Json(Repository())
        );

        RunRerunResult result = await this.RerunAsync(upstream);

        AssertRefused(result: result, rule: "default branch");
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ARunWithoutABranchIsRefusedAsync(string? branch)
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(Run(branch: branch)),
            static () => Responses.Json(Repository())
        );

        RunRerunResult result = await this.RerunAsync(upstream);

        AssertRefused(result: result, rule: "not an open pull request head");
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("in_progress")]
    public async Task ARunThatIsNotTheHeadOfAnOpenPullRequestIsRefusedAsync(string status)
    {
        foreach (string pulls in NotTheHead())
        {
            using RecordingHttpMessageHandler upstream = Respond(
                () => Responses.Json(Run(status: status)),
                static () => Responses.Json(Repository()),
                () => Responses.Json(pulls)
            );

            RunRerunResult result = await this.RerunAsync(upstream);

            AssertRefused(result: result, rule: "not an open pull request head");
            Assert.Equal(expected: 3, actual: upstream.Requests.Count);
        }
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("in_progress")]
    [InlineData("waiting")]
    public async Task ARunThatHasNotCompletedIsPendingAsync(string status)
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(Run(status: status)),
            static () => Responses.Json(Repository()),
            static () => Responses.Json(PullRequests(PullRequest(80)))
        );

        RunRerunResult result = await this.RerunAsync(upstream);

        Assert.Equal(
            expected: new RunRerunResult(
                Outcome: RunRerunOutcome.Pending,
                Message: "the run 5 has not completed, and can be rerun once it has",
                Run: 5,
                Attempt: null,
                Rule: null
            ),
            actual: result
        );
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task APassingRunWithAFailedJobHasItsFailedJobsRerunAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            static () => Responses.Json(Run(conclusion: "success")),
            static () => Responses.Json(Repository()),
            static () => Responses.Json(PullRequests(PullRequest(80))),
            static () =>
                Responses.Json(
                    RunJson.Jobs(
                        2,
                        RunJson.Job(id: 20, runId: 5, conclusion: "success"),
                        RunJson.Job(id: 21, runId: 5, conclusion: "failure")
                    )
                ),
            static () => Rerunning()
        );

        RunRerunResult result = await this.RerunAsync(upstream);

        Assert.Equal(expected: RunRerunOutcome.Success, actual: result.Outcome);
        Assert.Equal(expected: "Rerunning failed jobs of run 5 (attempt 3)", actual: result.Message);
        Assert.Equal(expected: new Uri(JOBS), actual: upstream.Requests[3].RequestUri);
        Assert.Equal(
            expected: new Uri(API + "/actions/runs/5/rerun-failed-jobs"),
            actual: upstream.Requests[4].RequestUri
        );
        AssertRerunPosted(upstream);
    }

    [Fact]
    public async Task APassingRunWithARequiredCheckThatNeverRanIsRerunWholeAsync()
    {
        this.Requires([
            new RequiredCheck(Name: "zeta", AppId: null),
            new RequiredCheck(Name: "lint", AppId: null),
            new RequiredCheck(Name: "build", AppId: null),
        ]);
        using RecordingHttpMessageHandler upstream = PassingRun(
            pulls: PullRequests(PullRequest(90), PullRequest(80), PullRequest(number: 70, headSha: OTHER_SHA)),
            checkRuns: CheckRuns(CheckRun(id: 1, name: "build", appId: APP_ID)),
            statuses: NO_STATUSES
        );

        RunRerunResult result = await this.RerunAsync(upstream);

        Assert.Equal(
            expected: new RunRerunResult(
                Outcome: RunRerunOutcome.Success,
                Message: "Rerunning all jobs of run 5 (attempt 3): required check lint never ran",
                Run: 5,
                Attempt: 3,
                Rule: null
            ),
            actual: result
        );
        Assert.Equal(
            expected:
            [
                new Uri(API + "/actions/runs/5"),
                new Uri(API),
                new Uri(PULLS),
                new Uri(JOBS),
                new Uri(API + "/pulls/80"),
                new Uri(API + "/commits/" + SHA + "/check-runs?per_page=100&filter=all"),
                new Uri(API + "/commits/" + SHA + "/status?per_page=100"),
                new Uri(API + "/actions/runs/5/rerun"),
            ],
            actual: [.. upstream.Requests.Select(static request => request.RequestUri)]
        );
        AssertRerunPosted(upstream);
        await this
            ._reader.Received(1)
            .ReadRequiredChecksAsync(
                repository: REPOSITORY,
                branch: DEFAULT_BRANCH,
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AMissingCheckNameCannotStartANewLineAsync()
    {
        this.Requires([new RequiredCheck(Name: "lint\nnext", AppId: null)]);
        using RecordingHttpMessageHandler upstream = PassingRun(checkRuns: CheckRuns(), statuses: NO_STATUSES);

        RunRerunResult result = await this.RerunAsync(upstream);

        Assert.Equal(
            expected: "Rerunning all jobs of run 5 (attempt 3): required check lint next never ran",
            actual: result.Message
        );
    }

    [Fact]
    public async Task ACheckPinnedToAnAppIsNotReportedByAnotherAppAsync()
    {
        this.Requires([new RequiredCheck(Name: "build", AppId: APP_ID)]);
        using RecordingHttpMessageHandler upstream = PassingRun(
            checkRuns: CheckRuns(CheckRun(id: 1, name: "build", appId: 999)),
            statuses: NO_STATUSES
        );

        RunRerunResult result = await this.RerunAsync(upstream);

        Assert.Equal(
            expected: "Rerunning all jobs of run 5 (attempt 3): required check build never ran",
            actual: result.Message
        );
        Assert.Equal(expected: new Uri(API + "/actions/runs/5/rerun"), actual: upstream.Requests[^1].RequestUri);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("failure")]
    [InlineData("error")]
    [InlineData("success")]
    public async Task AStatusInAnyStateReportsTheCheckAsync(string state)
    {
        this.Requires([new RequiredCheck(Name: "build", AppId: APP_ID)]);
        using RecordingHttpMessageHandler upstream = PassingRun(
            checkRuns: CheckRuns(),
            statuses: "{\"statuses\":[{\"id\":7,\"context\":\"build\",\"state\":\"" + state + "\"}]}"
        );

        RunRerunResult result = await this.RerunAsync(upstream);

        AssertRefused(result: result, rule: "no failed jobs and no required check missing");
        Assert.Equal(expected: 7, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(APP_ID)]
    public async Task ACheckRunThatHasNotFinishedReportsTheCheckAsync(long? appId)
    {
        this.Requires([new RequiredCheck(Name: "build", AppId: appId)]);
        using RecordingHttpMessageHandler upstream = PassingRun(
            checkRuns: CheckRuns(
                "{\"id\":1,\"name\":\"build\",\"status\":\"in_progress\",\"conclusion\":null,\"app\":{\"id\":15368}}"
            ),
            statuses: NO_STATUSES
        );

        RunRerunResult result = await this.RerunAsync(upstream);

        AssertRefused(result: result, rule: "no failed jobs and no required check missing");
    }

    [Fact]
    public async Task APassingRunWithNoRequiredChecksIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = PassingRun();

        RunRerunResult result = await this.RerunAsync(upstream);

        AssertRefused(result: result, rule: "no failed jobs and no required check missing");
        Assert.Equal(expected: 5, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData("closed", BRANCH, SHA)]
    [InlineData("open", "other", SHA)]
    [InlineData("open", BRANCH, OTHER_SHA)]
    public async Task APullRequestThatNoLongerHasTheRunsHeadIsRefusedAsync(string state, string headRef, string sha)
    {
        using RecordingHttpMessageHandler upstream = PassingRun(pullRequest: () =>
            Responses.Json(PullRequest(number: 80, state: state, headRef: headRef, headSha: sha))
        );

        RunRerunResult result = await this.RerunAsync(upstream);

        AssertRefused(result: result, rule: "not an open pull request head");
        Assert.Equal(expected: 5, actual: upstream.Requests.Count);
        await this.AssertRequiredChecksNotReadAsync();
    }

    [Fact]
    public async Task APullRequestThatIsGoneIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = PassingRun(pullRequest: static () =>
            Responses.Status(HttpStatusCode.NotFound)
        );

        RunRerunResult result = await this.RerunAsync(upstream);

        AssertRefused(result: result, rule: "not an open pull request head");
        await this.AssertRequiredChecksNotReadAsync();
    }

    [Fact]
    public async Task APullRequestThatCannotBeReadAgainIsAFailureAsync()
    {
        using RecordingHttpMessageHandler upstream = PassingRun(pullRequest: static () =>
            Responses.Status(HttpStatusCode.Forbidden)
        );

        RunRerunFailedException exception = await Assert.ThrowsAsync<RunRerunFailedException>(() =>
            this.RerunAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
        await this.AssertRequiredChecksNotReadAsync();
    }

    [Theory]
    [InlineData("{\"number\":80,\"state\":\"open\",\"head\":{\"ref\":\"feature\",\"sha\":\"" + SHA + "\"}}")]
    [InlineData(
        "{\"number\":80,\"state\":\"open\",\"head\":{\"ref\":\"feature\",\"sha\":\""
            + SHA
            + "\"},\"base\":{\"ref\":\"\"}}"
    )]
    [InlineData(
        "{\"number\":80,\"state\":\"open\",\"head\":{\"ref\":\"feature\",\"sha\":\""
            + SHA
            + "\"},\"base\":{\"ref\":\"HEAD\"}}"
    )]
    public async Task APullRequestWithAnInvalidBaseBranchIsAFailureAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = PassingRun(pullRequest: () => Responses.Json(json));

        RunRerunFailedException exception = await Assert.ThrowsAsync<RunRerunFailedException>(() =>
            this.RerunAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        await this.AssertRequiredChecksNotReadAsync();
    }

    [Fact]
    public async Task AnEmptyCheckRunIsAFailureAsync()
    {
        this.Requires([new RequiredCheck(Name: "build", AppId: null)]);
        using RecordingHttpMessageHandler upstream = PassingRun(checkRuns: CheckRuns("null"), statuses: NO_STATUSES);

        RunRerunFailedException exception = await Assert.ThrowsAsync<RunRerunFailedException>(() =>
            this.RerunAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.Equal(expected: 7, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task UnreadableRequiredChecksAreAFailureAsync()
    {
        foreach (Exception failure in RequiredChecksFailures())
        {
            this._reader.ReadRequiredChecksAsync(
                    repository: Arg.Any<string>(),
                    branch: Arg.Any<string>(),
                    cancellationToken: Arg.Any<CancellationToken>()
                )
                .ThrowsAsync(failure);
            using RecordingHttpMessageHandler upstream = PassingRun();

            RunRerunFailedException exception = await Assert.ThrowsAsync<RunRerunFailedException>(() =>
                this.RerunAsync(upstream)
            );

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
            Assert.Equal(expected: 5, actual: upstream.Requests.Count);
        }
    }

    [Fact]
    public async Task MoreThanAThousandOpenPullRequestsOnTheBranchIsAFailureAsync()
    {
        string pulls = PullRequests([.. Enumerable.Range(start: 1, count: 1001).Select(static n => PullRequest(n))]);
        using RecordingHttpMessageHandler upstream = Respond(
            static () => Responses.Json(Run()),
            static () => Responses.Json(Repository()),
            () => Responses.Json(pulls)
        );

        RunRerunFailedException exception = await Assert.ThrowsAsync<RunRerunFailedException>(() =>
            this.RerunAsync(upstream)
        );

        Assert.Equal(
            expected: REPOSITORY + " has more than 1000 open pull requests on the branch of the run 5",
            actual: exception.Message
        );
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task AThousandOpenPullRequestsOnTheBranchAreJudgedAsync()
    {
        string pulls = PullRequests([.. Enumerable.Range(start: 1, count: 1000).Select(static n => PullRequest(n))]);
        using RecordingHttpMessageHandler upstream = Respond(
            static () => Responses.Json(Run()),
            static () => Responses.Json(Repository()),
            () => Responses.Json(pulls),
            static () => Rerunning()
        );

        RunRerunResult result = await this.RerunAsync(upstream);

        Assert.Equal(expected: RunRerunOutcome.Success, actual: result.Outcome);
    }

    [Fact]
    public async Task ARerunRequestThatIsRejectedIsAFailureAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            static () => Responses.Json(Run()),
            static () => Responses.Json(Repository()),
            static () => Responses.Json(PullRequests(PullRequest(80))),
            static () => Responses.Status(HttpStatusCode.Forbidden)
        );

        RunRerunFailedException exception = await Assert.ThrowsAsync<RunRerunFailedException>(() =>
            this.RerunAsync(upstream)
        );

        Assert.Equal(expected: REQUEST_FAILED, actual: exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task ARerunRequestWithAnInvalidResponseIsAFailureAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            static () => Responses.Json(Run()),
            static () => Responses.Json(Repository()),
            static () => Responses.Json(PullRequests(PullRequest(80))),
            static () => Responses.Status(HttpStatusCode.Created)
        );

        RunRerunFailedException exception = await Assert.ThrowsAsync<RunRerunFailedException>(() =>
            this.RerunAsync(upstream)
        );

        Assert.Equal(expected: REQUEST_FAILED, actual: exception.Message);
        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task AMissingRunIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            static () => Responses.Status(HttpStatusCode.NotFound),
            static () => Responses.Json(Repository())
        );

        RunRerunResult result = await this.RerunAsync(upstream);

        Assert.Equal(
            expected: new RunRerunResult(
                Outcome: RunRerunOutcome.NotFound,
                Message: "the run 5 was not found in " + REPOSITORY,
                Run: 5,
                Attempt: null,
                Rule: null
            ),
            actual: result
        );
    }

    [Fact]
    public async Task AMissingRepositoryIsAFailureAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        RunRerunFailedException exception = await Assert.ThrowsAsync<RunRerunFailedException>(() =>
            this.RerunAsync(upstream)
        );

        Assert.Equal(
            expected: "the repository " + REPOSITORY + " was not found, or the caller cannot see it",
            actual: exception.Message
        );
        Assert.IsType<RepositoryNotFoundException>(exception.InnerException);
    }

    [Fact]
    public async Task ARepositoryThatGoesMissingIsAFailureAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            static () => Responses.Json(Run()),
            static () => Responses.Status(HttpStatusCode.NotFound)
        );

        RunRerunFailedException exception = await Assert.ThrowsAsync<RunRerunFailedException>(() =>
            this.RerunAsync(upstream)
        );

        Assert.Equal(
            expected: "the repository " + REPOSITORY + " was not found, or the caller cannot see it",
            actual: exception.Message
        );
    }

    [Theory]
    [InlineData("{\"id\":6,\"run_attempt\":1}")]
    [InlineData("{\"id\":5}")]
    [InlineData("{\"id\":5,\"run_attempt\":0}")]
    public async Task AnInvalidRunIsAFailureAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = Respond(() => Responses.Json(json));

        RunRerunFailedException exception = await Assert.ThrowsAsync<RunRerunFailedException>(() =>
            this.RerunAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("{\"full_name\":\"credfeto/credfeto-workflow-mcp\"}")]
    [InlineData("{\"full_name\":\"credfeto/credfeto-workflow-mcp\",\"default_branch\":\"\"}")]
    public async Task ARepositoryWithoutADefaultBranchIsAFailureAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = Respond(
            static () => Responses.Json(Run()),
            () => Responses.Json(json)
        );

        RunRerunFailedException exception = await Assert.ThrowsAsync<RunRerunFailedException>(() =>
            this.RerunAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-sha")]
    public async Task ARunWithAnInvalidHeadCommitIsAFailureAsync(string? sha)
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(Run(sha: sha)),
            static () => Responses.Json(Repository())
        );

        RunRerunFailedException exception = await Assert.ThrowsAsync<RunRerunFailedException>(() =>
            this.RerunAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[{\"state\":\"open\",\"head\":{\"ref\":\"feature\",\"sha\":\"" + SHA + "\"}}]")]
    [InlineData("[{\"number\":0,\"state\":\"open\",\"head\":{\"ref\":\"feature\",\"sha\":\"" + SHA + "\"}}]")]
    public async Task AnInvalidPullRequestIsAFailureAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = Respond(
            static () => Responses.Json(Run()),
            static () => Responses.Json(Repository()),
            () => Responses.Json(json)
        );

        RunRerunFailedException exception = await Assert.ThrowsAsync<RunRerunFailedException>(() =>
            this.RerunAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }

    private static IReadOnlyList<string> NotTheHead()
    {
        return
        [
            PullRequests(),
            PullRequests(PullRequest(number: 80, headRef: "other")),
            PullRequests(PullRequest(number: 80, headSha: OTHER_SHA)),
            "[{\"number\":80,\"state\":\"open\"}]",
        ];
    }

    private static IReadOnlyList<Exception> RequiredChecksFailures()
    {
        return
        [
            new RequiredChecksUnreadableException("could not read the required checks of " + DEFAULT_BRANCH),
            new BranchNotFoundException("the branch " + DEFAULT_BRANCH + " was not found in " + REPOSITORY),
            new RepositoryNotFoundException("the repository " + REPOSITORY + " was not found"),
        ];
    }

    private void Requires(IReadOnlyList<RequiredCheck> required)
    {
        Configure(
            call: this._reader.ReadRequiredChecksAsync(
                repository: Arg.Any<string>(),
                branch: Arg.Any<string>(),
                cancellationToken: Arg.Any<CancellationToken>()
            ),
            result: new(required)
        );
    }

    private static void Configure<T>(ValueTask<T> call, ValueTask<T> result)
    {
        _ = call.Returns(result);
    }

    private async Task AssertRequiredChecksNotReadAsync()
    {
        _ = await this
            ._reader.DidNotReceive()
            .ReadRequiredChecksAsync(
                repository: Arg.Any<string>(),
                branch: Arg.Any<string>(),
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    private async Task<RunRerunResult> RerunAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        string run = "5"
    )
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return await new RunRerunService(restClient: rest, requiredChecksReader: this._reader).RerunAsync(
            repository: repository,
            run: run,
            cancellationToken: this.CancellationToken()
        );
    }

    // A passing run on an open pull request head, so the jobs are read and then the pull request again
    private static RecordingHttpMessageHandler PassingRun(
        string? pulls = null,
        Func<HttpResponseMessage>? pullRequest = null,
        string? checkRuns = null,
        string? statuses = null
    )
    {
        List<Func<HttpResponseMessage>> responses =
        [
            static () => Responses.Json(Run(conclusion: "success")),
            static () => Responses.Json(Repository()),
            () => Responses.Json(pulls ?? PullRequests(PullRequest(80))),
            static () => Responses.Json(RunJson.Jobs(1, RunJson.Job(id: 20, runId: 5, conclusion: "success"))),
            pullRequest ?? (static () => Responses.Json(PullRequest(80))),
        ];

        if (checkRuns is not null)
        {
            responses.Add(() => Responses.Json(checkRuns));
        }

        if (statuses is not null)
        {
            responses.Add(() => Responses.Json(statuses));
            responses.Add(static () => Rerunning());
        }

        return Respond([.. responses]);
    }

    private static RecordingHttpMessageHandler Respond(params Func<HttpResponseMessage>[] responses)
    {
        return new(index =>
            index < responses.Length
                ? responses[index]()
                : throw new InvalidOperationException("Unexpected request " + Number(index))
        );
    }

    private static HttpResponseMessage Rerunning()
    {
        return Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: "{}");
    }

    private static void AssertRefused(RunRerunResult result, string rule)
    {
        Assert.Equal(
            expected: new RunRerunResult(
                Outcome: RunRerunOutcome.Refused,
                Message: "rerun refused: " + rule,
                Run: 5,
                Attempt: null,
                Rule: rule
            ),
            actual: result
        );
    }

    private static void AssertRerunPosted(RecordingHttpMessageHandler upstream)
    {
        RecordedRequest request = upstream.Requests[^1];

        Assert.Equal(expected: HttpMethod.Post, actual: request.Method);
        Assert.Equal(expected: RERUN_BODY, actual: request.Body);
    }

    private static string Run(
        string? branch = BRANCH,
        string? sha = SHA,
        string status = "completed",
        string conclusion = "failure",
        int attempt = 2
    )
    {
        string branchJson = branch is null ? string.Empty : ",\"head_branch\":\"" + branch + "\"";
        string shaJson = sha is null ? string.Empty : ",\"head_sha\":\"" + sha + "\"";

        return "{\"id\":5,\"name\":\"Build\",\"status\":\""
            + status
            + "\",\"conclusion\":\""
            + conclusion
            + "\""
            + shaJson
            + branchJson
            + ",\"event\":\"pull_request\",\"run_attempt\":"
            + Number(attempt)
            + "}";
    }

    private static string Repository()
    {
        return "{\"full_name\":\"" + REPOSITORY + "\",\"default_branch\":\"" + DEFAULT_BRANCH + "\"}";
    }

    private static string PullRequest(int number, string state = "open", string headRef = BRANCH, string headSha = SHA)
    {
        return "{\"number\":"
            + Number(number)
            + ",\"state\":\""
            + state
            + "\",\"head\":{\"ref\":\""
            + headRef
            + "\",\"sha\":\""
            + headSha
            + "\"},\"base\":{\"ref\":\""
            + DEFAULT_BRANCH
            + "\",\"sha\":\""
            + OTHER_SHA
            + "\"}}";
    }

    private static string PullRequests(params string[] pullRequests)
    {
        return "[" + string.Join(separator: ',', value: pullRequests) + "]";
    }

    private static string CheckRuns(params string[] runs)
    {
        return "{\"check_runs\":[" + string.Join(separator: ',', value: runs) + "]}";
    }

    private static string CheckRun(long id, string name, long appId)
    {
        return "{\"id\":"
            + Number(id)
            + ",\"name\":\""
            + name
            + "\",\"status\":\"completed\",\"conclusion\":\"success\",\"app\":{\"id\":"
            + Number(appId)
            + "}}";
    }

    private static string Number(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
