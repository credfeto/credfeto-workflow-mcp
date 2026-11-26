using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using FunFair.Test.Common;
using ModelContextProtocol;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class WorkflowMcpPrChecksWaitToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PULL_REQUEST = 176;
    private const string SHA = "0123456789abcdef0123456789abcdef01234567";
    private const string BRANCH = "main";
    private const int DEFAULT_TIMEOUT_MINUTES = 3;

    private readonly IProgress<ProgressNotificationValue> _progress;
    private readonly IPrChecksWaitService _service;

    public WorkflowMcpPrChecksWaitToolsTests()
    {
        this._service = GetSubstitute<IPrChecksWaitService>();
        this._progress = GetSubstitute<IProgress<ProgressNotificationValue>>();
    }

    [Fact]
    public async Task PrChecksWaitReturnsWhatTheServiceAnsweredAsync()
    {
        PrChecksWaitResponse expected = Response(PrChecksWaitOutcome.Passed);
        this.MockPrChecksWaitServiceWait(expected);

        PrChecksWaitResponse actual = await this.PrChecksWaitAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task PrChecksWaitPassesShaBranchAllAndTimeoutThroughAsync()
    {
        PrChecksWaitResponse expected = Response(PrChecksWaitOutcome.Pending);
        this._service.WaitAsync(
                repository: REPOSITORY,
                pullRequest: null,
                sha: SHA,
                branch: BRANCH,
                all: true,
                timeoutMinutes: 5,
                progress: Arg.Any<IProgress<PrChecksResponse>>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        PrChecksWaitResponse actual = await WorkflowMcpTools.PrChecksWaitAsync(
            repo: REPOSITORY,
            service: this._service,
            progress: this._progress,
            sha: SHA,
            branch: BRANCH,
            all: true,
            timeoutMinutes: 5,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task PrChecksWaitReportsEachReadToTheClientAsync()
    {
        PrChecksWaitResponse expected = Response(PrChecksWaitOutcome.Failed);
        this._service.WaitAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                sha: null,
                branch: null,
                all: false,
                timeoutMinutes: DEFAULT_TIMEOUT_MINUTES,
                progress: Arg.Any<IProgress<PrChecksResponse>>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                IProgress<PrChecksResponse> progress = call.ArgAt<IProgress<PrChecksResponse>>(6);
                progress.Report(Checks(PrChecksVerdict.Pending));
                progress.Report(Checks(PrChecksVerdict.Failed));

                return expected;
            });

        List<ProgressNotificationValue> reported = [];
        this._progress.When(progress => progress.Report(Arg.Any<ProgressNotificationValue>()))
            .Do(call => reported.Add(call.Arg<ProgressNotificationValue>()));

        await this.PrChecksWaitAsync();

        Assert.Equal(expected: [1f, 2f], actual: reported.Select(value => value.Progress));
        Assert.Equal(
            expected: ["read 1: Pending on " + SHA, "read 2: Failed on " + SHA],
            actual: reported.Select(value => value.Message),
            comparer: StringComparer.Ordinal
        );
    }

    [Fact]
    public Task PrChecksWaitReadFailureIsReportedToTheCallerAsync()
    {
        return this.AssertPrChecksWaitFailureReportedAsync(
            new PrChecksWaitFailedException(
                "the branch "
                    + BRANCH
                    + " was not found in "
                    + REPOSITORY
                    + "; the wait stopped after 2 reads over 30 seconds"
            )
        );
    }

    [Fact]
    public Task PrChecksWaitInvalidParameterIsReportedToTheCallerAsync()
    {
        return this.AssertPrChecksWaitFailureReportedAsync(
            ParameterValidationException.For(
                parameterName: "timeoutMinutes",
                rule: "it must be a whole number from 1 to 5"
            )
        );
    }

    [Fact]
    public async Task PrChecksWaitOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.MockPrChecksWaitServiceWaitFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.PrChecksWaitAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private async Task AssertPrChecksWaitFailureReportedAsync(Exception failure)
    {
        this.MockPrChecksWaitServiceWaitFailsWith(failure);

        McpException exception = await Assert.ThrowsAsync<McpException>(this.PrChecksWaitAsync);

        Assert.Equal(expected: failure.Message, actual: exception.Message);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    private void MockPrChecksWaitServiceWait(PrChecksWaitResponse response)
    {
        this._service.WaitAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                sha: null,
                branch: null,
                all: false,
                timeoutMinutes: DEFAULT_TIMEOUT_MINUTES,
                progress: Arg.Any<IProgress<PrChecksResponse>>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(response);
    }

    private void MockPrChecksWaitServiceWaitFailsWith(Exception failure)
    {
        this._service.WaitAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                sha: null,
                branch: null,
                all: false,
                timeoutMinutes: DEFAULT_TIMEOUT_MINUTES,
                progress: Arg.Any<IProgress<PrChecksResponse>>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<PrChecksWaitResponse> PrChecksWaitAsync()
    {
        return WorkflowMcpTools.PrChecksWaitAsync(
            repo: REPOSITORY,
            service: this._service,
            progress: this._progress,
            pr: PULL_REQUEST,
            cancellationToken: this.CancellationToken()
        );
    }

    private static PrChecksWaitResponse Response(PrChecksWaitOutcome outcome)
    {
        return new(Outcome: outcome, Reads: 2, WaitedSeconds: 15, LastRead: Checks(PrChecksVerdict.Passed));
    }

    private static PrChecksResponse Checks(PrChecksVerdict verdict)
    {
        return new(
            Repo: REPOSITORY,
            Sha: SHA,
            Verdict: verdict,
            Passed: 0,
            Failed: 0,
            Pending: 0,
            NotReported: 0,
            RunningCheckRuns: 0,
            Checks: []
        );
    }
}
