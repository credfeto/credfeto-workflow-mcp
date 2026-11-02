using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Checks;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using NSubstitute;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Checks;

public sealed class PrChecksServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string SHA = "0123456789abcdef0123456789abcdef01234567";
    private const string BASE_SHA = "fedcba9876543210fedcba9876543210fedcba98";
    private const long APP_ID = 15368;
    private const long OTHER_APP_ID = 999;
    private const string COMPLETED_AT = "2026-01-01T00:00:00Z";
    private const string UPDATED_AT = "2026-01-02T00:00:00Z";
    private const string REPOSITORY_JSON = """{"full_name":"credfeto/credfeto-workflow-mcp","default_branch":"main"}""";
    private const string NO_RUNS = """{"check_runs":[]}""";
    private const string NO_STATUSES = """{"statuses":[]}""";

    private static readonly IReadOnlyList<RequiredCheck> NothingRequired = [];

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(1, SHA, null)]
    [InlineData(1, null, "main")]
    [InlineData(null, SHA, "main")]
    [InlineData(1, SHA, "main")]
    public async Task ReadAsyncRejectsAnythingButExactlyOneTargetAsync(int? pullRequest, string? sha, string? branch)
    {
        ParameterValidationException exception = await this.AssertInvalidAsync(
            repository: REPOSITORY,
            pullRequest: pullRequest,
            sha: sha,
            branch: branch
        );

        Assert.Equal(expected: "pr", actual: exception.ParameterName);
        Assert.Equal(expected: "exactly one of pr, sha or branch must be given", actual: exception.Rule);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ReadAsyncRejectsAPullRequestNumberThatIsNotAboveZeroAsync(int pullRequest)
    {
        ParameterValidationException exception = await this.AssertInvalidAsync(
            repository: REPOSITORY,
            pullRequest: pullRequest,
            sha: null,
            branch: null
        );

        Assert.Equal(expected: "pullRequest", actual: exception.ParameterName);
        Assert.Equal(expected: "must be above 0", actual: exception.Rule);
    }

    [Fact]
    public async Task ReadAsyncRejectsAnInvalidShaAsync()
    {
        ParameterValidationException exception = await this.AssertInvalidAsync(
            repository: REPOSITORY,
            pullRequest: null,
            sha: "not-a-sha",
            branch: null
        );

        Assert.Equal(expected: "sha", actual: exception.ParameterName);
    }

    [Theory]
    [InlineData("-bad")]
    [InlineData("HEAD")]
    public async Task ReadAsyncRejectsAnInvalidBranchAsync(string branch)
    {
        ParameterValidationException exception = await this.AssertInvalidAsync(
            repository: REPOSITORY,
            pullRequest: null,
            sha: null,
            branch: branch
        );

        Assert.Equal(expected: "branch", actual: exception.ParameterName);
    }

    [Fact]
    public async Task ReadAsyncRejectsAnInvalidRepositoryAsync()
    {
        ParameterValidationException exception = await this.AssertInvalidAsync(
            repository: "not-a-repository",
            pullRequest: null,
            sha: SHA,
            branch: null
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
    }

    [Fact]
    public async Task ReadAsyncJudgesAPullRequestByTheRulesOfItsBaseBranchAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(PullRequestJson(headSha: "\"" + SHA + "\"", baseRef: "\"main\"")),
            () => Responses.Json(Runs(Run(id: 1, name: "build", conclusion: "success"))),
            () => Responses.Json(NO_STATUSES)
        );
        IRequiredChecksReader reader = Reader(NothingRequired);

        PrChecksResponse response = await this.CreateService(upstream: upstream, reader: reader)
            .ReadAsync(
                repository: REPOSITORY,
                pullRequest: 80,
                sha: null,
                branch: null,
                all: false,
                cancellationToken: this.CancellationToken()
            );

        Assert.Equal(expected: REPOSITORY, actual: response.Repo);
        Assert.Equal(expected: SHA, actual: response.Sha);
        Assert.Equal(expected: PrChecksVerdict.Passed, actual: response.Verdict);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
        AssertRequested(upstream: upstream, index: 0, relativeUri: "repos/" + REPOSITORY + "/pulls/80");
        AssertChecksRequested(upstream: upstream, sha: SHA, firstIndex: 1);
        await reader
            .Received(1)
            .ReadRequiredChecksAsync(
                repository: REPOSITORY,
                branch: "main",
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ReadAsyncJudgesACommitByTheRulesOfTheDefaultBranchAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(REPOSITORY_JSON),
            () => Responses.Json(NO_RUNS),
            () => Responses.Json(NO_STATUSES)
        );
        IRequiredChecksReader reader = Reader(NothingRequired);

        PrChecksResponse response = await this.CreateService(upstream: upstream, reader: reader)
            .ReadAsync(
                repository: REPOSITORY,
                pullRequest: null,
                sha: SHA.ToUpperInvariant(),
                branch: null,
                all: false,
                cancellationToken: this.CancellationToken()
            );

        Assert.Equal(expected: SHA, actual: response.Sha);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
        AssertRequested(upstream: upstream, index: 0, relativeUri: "repos/" + REPOSITORY);
        AssertChecksRequested(upstream: upstream, sha: SHA, firstIndex: 1);
        await reader
            .Received(1)
            .ReadRequiredChecksAsync(
                repository: REPOSITORY,
                branch: "main",
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ReadAsyncJudgesABranchByItsOwnRulesAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(NO_RUNS),
            () => Responses.Json(NO_STATUSES)
        );
        IRequiredChecksReader reader = Reader(NothingRequired);
        ConfigureBranch(reader: reader, sha: SHA);

        PrChecksResponse response = await this.CreateService(upstream: upstream, reader: reader)
            .ReadAsync(
                repository: REPOSITORY,
                pullRequest: null,
                sha: null,
                branch: "feature/checks",
                all: false,
                cancellationToken: this.CancellationToken()
            );

        Assert.Equal(expected: SHA, actual: response.Sha);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        AssertChecksRequested(upstream: upstream, sha: SHA, firstIndex: 0);
        await reader
            .Received(1)
            .ReadBranchAsync(
                repository: REPOSITORY,
                branch: "feature/checks",
                cancellationToken: Arg.Any<CancellationToken>()
            );
        await reader
            .Received(1)
            .ReadRequiredChecksAsync(
                repository: REPOSITORY,
                branch: "feature/checks",
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ReadAsyncReportsAPullRequestThatWasNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(() => Responses.Status(HttpStatusCode.NotFound));

        PullRequestNotFoundException exception = await Assert.ThrowsAsync<PullRequestNotFoundException>(() =>
            this.CreateService(upstream: upstream, reader: Reader(NothingRequired))
                .ReadAsync(
                    repository: REPOSITORY,
                    pullRequest: 80,
                    sha: null,
                    branch: null,
                    all: false,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Equal(expected: "pull request " + REPOSITORY + "#80 was not found", actual: exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task ReadAsyncReportsARepositoryThatWasNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(() => Responses.Status(HttpStatusCode.NotFound));

        RepositoryNotFoundException exception = await Assert.ThrowsAsync<RepositoryNotFoundException>(() =>
            this.CreateService(upstream: upstream, reader: Reader(NothingRequired))
                .ReadAsync(
                    repository: REPOSITORY,
                    pullRequest: null,
                    sha: SHA,
                    branch: null,
                    all: false,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Equal(
            expected: "the repository " + REPOSITORY + " was not found, or the caller cannot see it",
            actual: exception.Message
        );
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task ReadAsyncReportsACommitThatWasNotFoundAsync(HttpStatusCode statusCode)
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(REPOSITORY_JSON),
            () => Responses.Status(statusCode)
        );

        CommitNotFoundException exception = await Assert.ThrowsAsync<CommitNotFoundException>(() =>
            this.ReadCommitAsync(upstream: upstream, required: NothingRequired).AsTask()
        );

        Assert.Equal(expected: "the commit " + SHA + " was not found in " + REPOSITORY, actual: exception.Message);
    }

    [Fact]
    public async Task ReadAsyncReportsAFailedCheckRunsReadAsUnreadableAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(REPOSITORY_JSON),
            () => Responses.Status(HttpStatusCode.Unauthorized)
        );

        PrChecksUnreadableException exception = await this.AssertUnreadableAsync(
            upstream: upstream,
            required: NothingRequired
        );

        Assert.Equal(
            expected: "could not read the checks of " + REPOSITORY + " commit " + SHA,
            actual: exception.Message
        );
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.Unauthorized, actual: inner.StatusCode);
    }

    [Fact]
    public async Task ReadAsyncReportsAFailedStatusReadAsUnreadableAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(REPOSITORY_JSON),
            () => Responses.Json(NO_RUNS),
            () => Responses.Status(HttpStatusCode.InternalServerError)
        );

        PrChecksUnreadableException exception = await this.AssertUnreadableAsync(
            upstream: upstream,
            required: NothingRequired
        );

        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task ReadAsyncReportsAPullRequestReadThatFailedAsUnreadableAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(() => Responses.Status(HttpStatusCode.Unauthorized));

        PrChecksUnreadableException exception = await Assert.ThrowsAsync<PrChecksUnreadableException>(() =>
            this.CreateService(upstream: upstream, reader: Reader(NothingRequired))
                .ReadAsync(
                    repository: REPOSITORY,
                    pullRequest: 80,
                    sha: null,
                    branch: null,
                    all: false,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Equal(
            expected: "could not read the checks of " + REPOSITORY + " pull request #80",
            actual: exception.Message
        );
    }

    [Fact]
    public async Task ReadAsyncReportsAResponseThatIsNotJsonAsUnreadableAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(() => Responses.Html("<html></html>"));

        PrChecksUnreadableException exception = await this.AssertUnreadableAsync(
            upstream: upstream,
            required: NothingRequired
        );

        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Theory]
    [InlineData("\"ABCDEF0123456789ABCDEF0123456789ABCDEF01\"")]
    [InlineData("\"0123\"")]
    [InlineData("null")]
    public async Task ReadAsyncRejectsAPullRequestWithAnInvalidHeadAsync(string headSha)
    {
        using RecordingHttpMessageHandler upstream = Respond(() =>
            Responses.Json(PullRequestJson(headSha: headSha, baseRef: "\"main\""))
        );

        PrChecksUnreadableException exception = await this.AssertPullRequestUnreadableAsync(upstream);

        AssertInnerMessage(exception: exception, expected: "GitHub returned an invalid head commit");
    }

    [Fact]
    public async Task ReadAsyncRejectsAPullRequestWithNoHeadAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(() => Responses.Json("""{"base":{"ref":"main"}}"""));

        PrChecksUnreadableException exception = await this.AssertPullRequestUnreadableAsync(upstream);

        AssertInnerMessage(exception: exception, expected: "GitHub returned an invalid head commit");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    public async Task ReadAsyncRejectsAPullRequestWithNoBaseBranchAsync(string baseRef)
    {
        using RecordingHttpMessageHandler upstream = Respond(() =>
            Responses.Json(PullRequestJson(headSha: "\"" + SHA + "\"", baseRef: baseRef))
        );

        PrChecksUnreadableException exception = await this.AssertPullRequestUnreadableAsync(upstream);

        AssertInnerMessage(exception: exception, expected: "GitHub returned no branch to read the required checks of");
    }

    [Fact]
    public async Task ReadAsyncRejectsAPullRequestWithNoBaseAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(() =>
            Responses.Json("{\"head\":{\"sha\":\"" + SHA + "\"}}")
        );

        PrChecksUnreadableException exception = await this.AssertPullRequestUnreadableAsync(upstream);

        AssertInnerMessage(exception: exception, expected: "GitHub returned no branch to read the required checks of");
    }

    [Fact]
    public async Task ReadAsyncRejectsAPullRequestWithAnInvalidBaseBranchAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(() =>
            Responses.Json(PullRequestJson(headSha: "\"" + SHA + "\"", baseRef: "\"-bad\""))
        );

        PrChecksUnreadableException exception = await this.AssertPullRequestUnreadableAsync(upstream);

        GitHubInvalidResponseException inner = AssertInnerMessage(
            exception: exception,
            expected: "GitHub returned an invalid branch to read the required checks of"
        );
        Assert.IsType<ParameterValidationException>(inner.InnerException);
    }

    [Theory]
    [InlineData(
        """{"full_name":"credfeto/credfeto-workflow-mcp"}""",
        "GitHub returned no branch to read the required checks of"
    )]
    [InlineData(
        """{"full_name":"credfeto/credfeto-workflow-mcp","default_branch":""}""",
        "GitHub returned no branch to read the required checks of"
    )]
    [InlineData(
        """{"full_name":"credfeto/credfeto-workflow-mcp","default_branch":"HEAD"}""",
        "GitHub returned an invalid branch to read the required checks of"
    )]
    public async Task ReadAsyncRejectsARepositoryWithAnInvalidDefaultBranchAsync(string repositoryJson, string expected)
    {
        using RecordingHttpMessageHandler upstream = Respond(() => Responses.Json(repositoryJson));

        PrChecksUnreadableException exception = await this.AssertUnreadableAsync(
            upstream: upstream,
            required: NothingRequired
        );

        AssertInnerMessage(exception: exception, expected: expected);
    }

    [Fact]
    public async Task ReadAsyncRejectsABranchWithAnInvalidHeadAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond();
        IRequiredChecksReader reader = Reader(NothingRequired);
        ConfigureBranch(reader: reader, sha: "not-a-sha");

        PrChecksUnreadableException exception = await Assert.ThrowsAsync<PrChecksUnreadableException>(() =>
            this.CreateService(upstream: upstream, reader: reader)
                .ReadAsync(
                    repository: REPOSITORY,
                    pullRequest: null,
                    sha: null,
                    branch: "main",
                    all: false,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Equal(
            expected: "could not read the checks of " + REPOSITORY + " branch main",
            actual: exception.Message
        );
        AssertInnerMessage(exception: exception, expected: "GitHub returned an invalid head commit");
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("""{"check_runs":[null]}""", NO_STATUSES, "GitHub returned an empty check run")]
    [InlineData(NO_RUNS, """{"statuses":[null]}""", "GitHub returned an empty commit status")]
    [InlineData("""{"check_runs":[{"id":1}]}""", NO_STATUSES, "GitHub returned a check run without a name")]
    [InlineData("""{"check_runs":[{"id":1,"name":""}]}""", NO_STATUSES, "GitHub returned a check run without a name")]
    [InlineData("""{"check_runs":[{"name":"build"}]}""", NO_STATUSES, "GitHub returned a check run without an id")]
    [InlineData(
        NO_RUNS,
        """{"statuses":[{"id":1,"state":"success"}]}""",
        "GitHub returned a commit status without a name"
    )]
    [InlineData(
        NO_RUNS,
        """{"statuses":[{"context":"build","state":"success"}]}""",
        "GitHub returned a commit status without an id"
    )]
    [InlineData("""{"check_runs":null}""", NO_STATUSES, "GitHub returned a page with no items list")]
    [InlineData(NO_RUNS, """{"statuses":null}""", "GitHub returned a page with no items list")]
    public async Task ReadAsyncRejectsAnInvalidCheckAsync(string runs, string statuses, string expected)
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(REPOSITORY_JSON),
            () => Responses.Json(runs),
            () => Responses.Json(statuses)
        );

        PrChecksUnreadableException exception = await this.AssertUnreadableAsync(
            upstream: upstream,
            required: NothingRequired
        );

        AssertInnerMessage(exception: exception, expected: expected);
    }

    [Fact]
    public async Task ReadAsyncJudgesEveryReportedCheckWhenNothingIsRequiredAsync()
    {
        PrChecksResponse response = await this.ReadCommitAsync(
            required: NothingRequired,
            runs: Runs(
                Run(id: 1, name: "build", conclusion: "success"),
                Run(id: 2, name: "test", conclusion: "failure"),
                Run(id: 3, name: "lint", conclusion: null),
                Run(id: 4, name: "docs", conclusion: "skipped"),
                Run(id: 5, name: "format", conclusion: "neutral"),
                Run(id: 6, name: "deploy", conclusion: "cancelled"),
                Run(id: 7, name: "coverage", conclusion: "")
            ),
            statuses: Statuses(
                Status(id: 11, context: "ci/passed", state: "success"),
                Status(id: 12, context: "ci/pending", state: "pending"),
                Status(id: 13, context: "ci/error", state: "error"),
                Status(id: 14, context: "ci/failure", state: "failure")
            ),
            all: false
        );

        PrCheckResult[] expected =
        [
            new(Name: "build", Verdict: PrChecksVerdict.Passed, CompletedAt: COMPLETED_AT, RunId: 1),
            new(Name: "ci/error", Verdict: PrChecksVerdict.Failed, CompletedAt: UPDATED_AT, RunId: 13),
            new(Name: "ci/failure", Verdict: PrChecksVerdict.Failed, CompletedAt: UPDATED_AT, RunId: 14),
            new(Name: "ci/passed", Verdict: PrChecksVerdict.Passed, CompletedAt: UPDATED_AT, RunId: 11),
            new(Name: "ci/pending", Verdict: PrChecksVerdict.Pending, CompletedAt: null, RunId: null),
            new(Name: "coverage", Verdict: PrChecksVerdict.Pending, CompletedAt: null, RunId: null),
            new(Name: "deploy", Verdict: PrChecksVerdict.Failed, CompletedAt: COMPLETED_AT, RunId: 6),
            new(Name: "docs", Verdict: PrChecksVerdict.Passed, CompletedAt: COMPLETED_AT, RunId: 4),
            new(Name: "format", Verdict: PrChecksVerdict.Passed, CompletedAt: COMPLETED_AT, RunId: 5),
            new(Name: "lint", Verdict: PrChecksVerdict.Pending, CompletedAt: null, RunId: null),
            new(Name: "test", Verdict: PrChecksVerdict.Failed, CompletedAt: COMPLETED_AT, RunId: 2),
        ];

        Assert.Equal(expected: PrChecksVerdict.Failed, actual: response.Verdict);
        Assert.Equal(expected: expected, actual: response.Checks);
        Assert.Equal(expected: 4, actual: response.Passed);
        Assert.Equal(expected: 4, actual: response.Failed);
        Assert.Equal(expected: 3, actual: response.Pending);
    }

    [Fact]
    public async Task ReadAsyncIsPendingWhenNothingFailedButSomethingIsPendingAsync()
    {
        PrChecksResponse response = await this.ReadCommitAsync(
            required: NothingRequired,
            runs: Runs(Run(id: 1, name: "build", conclusion: "success"), Run(id: 2, name: "test", conclusion: null)),
            statuses: NO_STATUSES,
            all: false
        );

        Assert.Equal(expected: PrChecksVerdict.Pending, actual: response.Verdict);
    }

    [Fact]
    public async Task ReadAsyncReportsNoChecksWhenNothingReportedEvenIfChecksAreRequiredAsync()
    {
        PrChecksResponse response = await this.ReadCommitAsync(
            required: [new(Name: "build", AppId: null)],
            runs: NO_RUNS,
            statuses: NO_STATUSES,
            all: false
        );

        Assert.Equal(expected: PrChecksVerdict.NoChecks, actual: response.Verdict);
        PrCheckResult check = Assert.Single(response.Checks);
        Assert.Equal(
            expected: new PrCheckResult(
                Name: "build",
                Verdict: PrChecksVerdict.Pending,
                CompletedAt: null,
                RunId: null
            ),
            actual: check
        );
        Assert.Equal(expected: 1, actual: response.Pending);
    }

    [Fact]
    public async Task ReadAsyncOnlyJudgesTheRequiredChecksAsync()
    {
        PrChecksResponse response = await this.ReadCommitAsync(
            required: [new(Name: "build", AppId: null), new(Name: "deploy", AppId: null)],
            runs: Runs(
                Run(id: 1, name: "build", conclusion: "success"),
                Run(id: 2, name: "extra", conclusion: "failure")
            ),
            statuses: NO_STATUSES,
            all: false
        );

        PrCheckResult[] expected =
        [
            new(Name: "build", Verdict: PrChecksVerdict.Passed, CompletedAt: COMPLETED_AT, RunId: 1),
            new(Name: "deploy", Verdict: PrChecksVerdict.Pending, CompletedAt: null, RunId: null),
        ];

        Assert.Equal(expected: PrChecksVerdict.Pending, actual: response.Verdict);
        Assert.Equal(expected: expected, actual: response.Checks);
    }

    [Fact]
    public async Task ReadAsyncJudgesTheRequiredAndEveryOtherReportedCheckWhenAllAreAskedForAsync()
    {
        PrChecksResponse response = await this.ReadCommitAsync(
            required: [new(Name: "build", AppId: APP_ID)],
            runs: Runs(
                Run(id: 1, name: "build", conclusion: "success"),
                Run(id: 2, name: "build", conclusion: "failure", appId: OTHER_APP_ID),
                Run(id: 3, name: "extra", conclusion: "failure", appId: OTHER_APP_ID)
            ),
            statuses: NO_STATUSES,
            all: true
        );

        Assert.Equal(expected: PrChecksVerdict.Failed, actual: response.Verdict);
        Assert.Equal(
            expected:
            [
                new(Name: "build (app 15368)", Verdict: PrChecksVerdict.Passed, CompletedAt: COMPLETED_AT, RunId: 1),
                new PrCheckResult(Name: "extra", Verdict: PrChecksVerdict.Failed, CompletedAt: COMPLETED_AT, RunId: 3),
            ],
            actual: response.Checks
        );
    }

    [Fact]
    public async Task ReadAsyncOnlyMeetsAPinnedCheckWithTheRunsOfItsAppAsync()
    {
        PrChecksResponse response = await this.ReadCommitAsync(
            required: [new(Name: "build", AppId: APP_ID)],
            runs: Runs(Run(id: 1, name: "build", conclusion: "success", appId: OTHER_APP_ID)),
            statuses: Statuses(Status(id: 2, context: "build", state: "success")),
            all: false
        );

        PrCheckResult check = Assert.Single(response.Checks);
        Assert.Equal(
            expected: new PrCheckResult(
                Name: "build (app 15368)",
                Verdict: PrChecksVerdict.Pending,
                CompletedAt: null,
                RunId: null
            ),
            actual: check
        );
    }

    [Fact]
    public async Task ReadAsyncFailsAPinnedCheckWhenAStatusOfTheSameNameFailedAsync()
    {
        PrChecksResponse response = await this.ReadCommitAsync(
            required: [new(Name: "build", AppId: APP_ID)],
            runs: Runs(Run(id: 1, name: "build", conclusion: "success")),
            statuses: Statuses(Status(id: 2, context: "build", state: "failure")),
            all: false
        );

        PrCheckResult check = Assert.Single(response.Checks);
        Assert.Equal(
            expected: new PrCheckResult(
                Name: "build (app 15368)",
                Verdict: PrChecksVerdict.Failed,
                CompletedAt: UPDATED_AT,
                RunId: 2
            ),
            actual: check
        );
    }

    [Fact]
    public async Task ReadAsyncJudgesAnUnpinnedCheckByItsWorstReportAsync()
    {
        PrChecksResponse response = await this.ReadCommitAsync(
            required: [new(Name: "build", AppId: null)],
            runs: Runs(
                Run(id: 1, name: "build", conclusion: "success"),
                Run(id: 2, name: "build", conclusion: "failure", appId: OTHER_APP_ID),
                Run(id: 3, name: "build", conclusion: "failure", appId: 7)
            ),
            statuses: Statuses(Status(id: 4, context: "build", state: "pending")),
            all: false
        );

        PrCheckResult check = Assert.Single(response.Checks);
        Assert.Equal(
            expected: new PrCheckResult(
                Name: "build",
                Verdict: PrChecksVerdict.Failed,
                CompletedAt: COMPLETED_AT,
                RunId: 3
            ),
            actual: check
        );
    }

    [Fact]
    public async Task ReadAsyncJudgesAnUnpinnedCheckAsPendingWhenItsWorstReportIsPendingAsync()
    {
        PrChecksResponse response = await this.ReadCommitAsync(
            required: [new(Name: "build", AppId: null)],
            runs: Runs(Run(id: 1, name: "build", conclusion: "success")),
            statuses: Statuses(Status(id: 2, context: "build", state: "pending")),
            all: false
        );

        PrCheckResult check = Assert.Single(response.Checks);
        Assert.Equal(
            expected: new PrCheckResult(
                Name: "build",
                Verdict: PrChecksVerdict.Pending,
                CompletedAt: null,
                RunId: null
            ),
            actual: check
        );
    }

    [Theory]
    [InlineData("failure", "success", PrChecksVerdict.Passed, 2)]
    [InlineData("failure", "skipped", PrChecksVerdict.Failed, 1)]
    [InlineData("success", "skipped", PrChecksVerdict.Passed, 1)]
    [InlineData("cancelled", "skipped", PrChecksVerdict.Passed, 2)]
    [InlineData("skipped", "skipped", PrChecksVerdict.Passed, 2)]
    [InlineData("success", "failure", PrChecksVerdict.Failed, 2)]
    public async Task ReadAsyncJudgesARepeatedRunByItsNewestReportAsync(
        string first,
        string second,
        PrChecksVerdict expectedVerdict,
        long expectedRunId
    )
    {
        // The newer run is listed first so the order of the response is shown not to matter
        PrChecksResponse response = await this.ReadCommitAsync(
            required: NothingRequired,
            runs: Runs(Run(id: 2, name: "build", conclusion: second), Run(id: 1, name: "build", conclusion: first)),
            statuses: NO_STATUSES,
            all: false
        );

        PrCheckResult check = Assert.Single(response.Checks);
        Assert.Equal(expected: expectedVerdict, actual: check.Verdict);
        Assert.Equal(expected: expectedRunId, actual: check.RunId);
    }

    [Fact]
    public async Task ReadAsyncKeepsCheckNamesOnOneLineAsync()
    {
        PrChecksResponse response = await this.ReadCommitAsync(
            required: [new(Name: "build\u2028one", AppId: APP_ID)],
            runs: """{"check_runs":[{"id":1,"name":"build\u2028one","status":"completed","conclusion":"success","app":{"id":15368}}]}""",
            statuses: NO_STATUSES,
            all: false
        );

        PrCheckResult check = Assert.Single(response.Checks);
        Assert.Equal(expected: "build one (app 15368)", actual: check.Name);
        Assert.Equal(expected: PrChecksVerdict.Passed, actual: check.Verdict);
    }

    [Fact]
    public async Task ReadAsyncTreatsARunWithNoAppAsUnpinnedAsync()
    {
        PrChecksResponse response = await this.ReadCommitAsync(
            required: [new(Name: "build", AppId: APP_ID)],
            runs: """{"check_runs":[{"id":1,"name":"build","status":"completed","conclusion":"success"}]}""",
            statuses: NO_STATUSES,
            all: false
        );

        PrCheckResult check = Assert.Single(response.Checks);
        Assert.Equal(expected: PrChecksVerdict.Pending, actual: check.Verdict);
    }

    [Fact]
    public async Task ReadAsyncReadsEveryPageOfCheckRunsAsync()
    {
        string secondPage =
            GitHubServices.ApiBaseUrl
            + "repos/"
            + REPOSITORY
            + "/commits/"
            + SHA
            + "/check-runs?per_page=100&filter=all&page=2";
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(REPOSITORY_JSON),
            () =>
                Responses.Json(
                    json: Runs(Run(id: 1, name: "build", conclusion: "success")),
                    nextLink: "<" + secondPage + ">; rel=\"next\""
                ),
            () => Responses.Json(Runs(Run(id: 2, name: "test", conclusion: "failure"))),
            () => Responses.Json(NO_STATUSES)
        );

        PrChecksResponse response = await this.ReadCommitAsync(upstream: upstream, required: NothingRequired);

        Assert.Equal(expected: 2, actual: response.Checks.Count);
        Assert.Equal(expected: PrChecksVerdict.Failed, actual: response.Verdict);
        Assert.Equal(expected: new Uri(secondPage), actual: upstream.Requests[2].RequestUri);
    }

    private async Task<ParameterValidationException> AssertInvalidAsync(
        string repository,
        int? pullRequest,
        string? sha,
        string? branch
    )
    {
        using RecordingHttpMessageHandler upstream = Respond();
        IRequiredChecksReader reader = Reader(NothingRequired);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateService(upstream: upstream, reader: reader)
                .ReadAsync(
                    repository: repository,
                    pullRequest: pullRequest,
                    sha: sha,
                    branch: branch,
                    all: false,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Empty(upstream.Requests);
        await reader
            .DidNotReceive()
            .ReadRequiredChecksAsync(
                repository: Arg.Any<string>(),
                branch: Arg.Any<string>(),
                cancellationToken: Arg.Any<CancellationToken>()
            );

        return exception;
    }

    private Task<PrChecksUnreadableException> AssertUnreadableAsync(
        RecordingHttpMessageHandler upstream,
        IReadOnlyList<RequiredCheck> required
    )
    {
        return Assert.ThrowsAsync<PrChecksUnreadableException>(() =>
            this.ReadCommitAsync(upstream: upstream, required: required).AsTask()
        );
    }

    private Task<PrChecksUnreadableException> AssertPullRequestUnreadableAsync(RecordingHttpMessageHandler upstream)
    {
        return Assert.ThrowsAsync<PrChecksUnreadableException>(() =>
            this.CreateService(upstream: upstream, reader: Reader(NothingRequired))
                .ReadAsync(
                    repository: REPOSITORY,
                    pullRequest: 80,
                    sha: null,
                    branch: null,
                    all: false,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );
    }

    private static GitHubInvalidResponseException AssertInnerMessage(
        PrChecksUnreadableException exception,
        string expected
    )
    {
        GitHubInvalidResponseException inner = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: expected, actual: inner.Message);

        return inner;
    }

    private async ValueTask<PrChecksResponse> ReadCommitAsync(
        IReadOnlyList<RequiredCheck> required,
        string runs,
        string statuses,
        bool all
    )
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(REPOSITORY_JSON),
            () => Responses.Json(runs),
            () => Responses.Json(statuses)
        );

        return await this.ReadCommitAsync(upstream: upstream, required: required, all: all);
    }

    private ValueTask<PrChecksResponse> ReadCommitAsync(
        RecordingHttpMessageHandler upstream,
        IReadOnlyList<RequiredCheck> required,
        bool all = false
    )
    {
        return this.CreateService(upstream: upstream, reader: Reader(required))
            .ReadAsync(
                repository: REPOSITORY,
                pullRequest: null,
                sha: SHA,
                branch: null,
                all: all,
                cancellationToken: this.CancellationToken()
            );
    }

    private PrChecksService CreateService(RecordingHttpMessageHandler upstream, IRequiredChecksReader reader)
    {
        GitHubRestClient client = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(restClient: client, requiredChecksReader: reader);
    }

    private static IRequiredChecksReader Reader(IReadOnlyList<RequiredCheck> required)
    {
        IRequiredChecksReader reader = GetSubstitute<IRequiredChecksReader>();
        Configure(
            call: reader.ReadRequiredChecksAsync(
                repository: Arg.Any<string>(),
                branch: Arg.Any<string>(),
                cancellationToken: Arg.Any<CancellationToken>()
            ),
            result: new(required)
        );

        return reader;
    }

    private static void ConfigureBranch(IRequiredChecksReader reader, string sha)
    {
        Configure(
            call: reader.ReadBranchAsync(
                repository: Arg.Any<string>(),
                branch: Arg.Any<string>(),
                cancellationToken: Arg.Any<CancellationToken>()
            ),
            result: new(new BranchInfo(Sha: sha, Checks: []))
        );
    }

    private static void Configure<T>(ValueTask<T> call, ValueTask<T> result)
    {
        _ = call.Returns(result);
    }

    private static RecordingHttpMessageHandler Respond(params Func<HttpResponseMessage>[] responses)
    {
        return new(index => responses[index]());
    }

    private static void AssertRequested(RecordingHttpMessageHandler upstream, int index, string relativeUri)
    {
        Assert.Equal(
            expected: new Uri(GitHubServices.ApiBaseUrl + relativeUri),
            actual: upstream.Requests[index].RequestUri
        );
    }

    private static void AssertChecksRequested(RecordingHttpMessageHandler upstream, string sha, int firstIndex)
    {
        AssertRequested(
            upstream: upstream,
            index: firstIndex,
            relativeUri: "repos/" + REPOSITORY + "/commits/" + sha + "/check-runs?per_page=100&filter=all"
        );
        AssertRequested(
            upstream: upstream,
            index: firstIndex + 1,
            relativeUri: "repos/" + REPOSITORY + "/commits/" + sha + "/status?per_page=100"
        );
    }

    private static string PullRequestJson(string headSha, string baseRef)
    {
        return "{\"head\":{\"ref\":\"feature\",\"sha\":"
            + headSha
            + "},\"base\":{\"ref\":"
            + baseRef
            + ",\"sha\":\""
            + BASE_SHA
            + "\"}}";
    }

    private static string Runs(params string[] runs)
    {
        return "{\"check_runs\":[" + string.Join(separator: ',', value: runs) + "]}";
    }

    private static string Statuses(params string[] statuses)
    {
        return "{\"statuses\":[" + string.Join(separator: ',', value: statuses) + "]}";
    }

    private static string Run(long id, string name, string? conclusion, long appId = APP_ID)
    {
        return conclusion is null
            ? $$$"""{"id":{{{id}}},"name":"{{{name}}}","status":"in_progress","conclusion":null,"completed_at":null,"app":{"id":{{{appId}}} }}"""
            : $$$"""{"id":{{{id}}},"name":"{{{name}}}","status":"completed","conclusion":"{{{conclusion}}}","completed_at":"{{{COMPLETED_AT}}}","app":{"id":{{{appId}}} }}""";
    }

    private static string Status(long id, string context, string state)
    {
        return $$"""{"id":{{id}},"context":"{{context}}","state":"{{state}}","updated_at":"{{UPDATED_AT}}"}""";
    }
}
