using System;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Checks;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using FunFair.Test.Common;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Checks;

public sealed class PrChecksWaitServiceTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PR = 176;
    private const string SHA = "0123456789abcdef0123456789abcdef01234567";
    private const string OTHER_SHA = "89abcdef0123456789abcdef0123456789abcdef";
    private const string BRANCH = "main";
    private const int ONE_MINUTE = 1;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly IPrChecksService _checks;
    private readonly SteppingTimeProvider _clock;
    private readonly IProgress<PrChecksResponse> _progress;
    private readonly IPrChecksWaitService _service;

    public PrChecksWaitServiceTests()
    {
        this._checks = GetSubstitute<IPrChecksService>();
        this._progress = GetSubstitute<IProgress<PrChecksResponse>>();
        this._clock = new();
        this._service = new PrChecksWaitService(prChecksService: this._checks, timeProvider: this._clock);
    }

    [Fact]
    public async Task APassConfirmedByTheNextReadOfTheSameHeadPassesAsync()
    {
        PrChecksResponse confirmed = Checks(PrChecksVerdict.Passed);
        this.MockPrChecksServiceRead(Checks(PrChecksVerdict.Passed), confirmed);

        PrChecksWaitResponse response = await this.WaitAsync();

        AssertOutcome(
            response: response,
            outcome: PrChecksWaitOutcome.Passed,
            reads: 2,
            waitedSeconds: 15,
            lastRead: confirmed
        );
        Assert.Equal(expected: [PollInterval], actual: this._clock.Delays);
    }

    [Fact]
    public async Task APassFollowedByAFailureFailsAsync()
    {
        PrChecksResponse failed = Checks(PrChecksVerdict.Failed);
        this.MockPrChecksServiceRead(Checks(PrChecksVerdict.Passed), failed);

        PrChecksWaitResponse response = await this.WaitAsync();

        AssertOutcome(
            response: response,
            outcome: PrChecksWaitOutcome.Failed,
            reads: 2,
            waitedSeconds: 15,
            lastRead: failed
        );
    }

    [Fact]
    public async Task APassOnANewHeadStartsTheConfirmationAgainAsync()
    {
        PrChecksResponse confirmed = Checks(verdict: PrChecksVerdict.Passed, sha: OTHER_SHA);
        this.MockPrChecksServiceRead(
            Checks(PrChecksVerdict.Passed),
            Checks(verdict: PrChecksVerdict.Passed, sha: OTHER_SHA),
            confirmed
        );

        PrChecksWaitResponse response = await this.WaitAsync();

        AssertOutcome(
            response: response,
            outcome: PrChecksWaitOutcome.Passed,
            reads: 3,
            waitedSeconds: 30,
            lastRead: confirmed
        );
    }

    [Fact]
    public async Task APassInterruptedByPendingChecksStartsTheConfirmationAgainAsync()
    {
        PrChecksResponse confirmed = Checks(PrChecksVerdict.Passed);
        this.MockPrChecksServiceRead(
            Checks(PrChecksVerdict.Passed),
            Checks(PrChecksVerdict.Pending),
            Checks(PrChecksVerdict.Passed),
            confirmed
        );

        PrChecksWaitResponse response = await this.WaitAsync();

        AssertOutcome(
            response: response,
            outcome: PrChecksWaitOutcome.Passed,
            reads: 4,
            waitedSeconds: 45,
            lastRead: confirmed
        );
    }

    [Fact]
    public async Task PendingChecksThatThenPassAreWaitedForAsync()
    {
        PrChecksResponse confirmed = Checks(PrChecksVerdict.Passed);
        this.MockPrChecksServiceRead(Checks(PrChecksVerdict.Pending), Checks(PrChecksVerdict.Passed), confirmed);

        PrChecksWaitResponse response = await this.WaitAsync();

        AssertOutcome(
            response: response,
            outcome: PrChecksWaitOutcome.Passed,
            reads: 3,
            waitedSeconds: 30,
            lastRead: confirmed
        );
    }

    [Fact]
    public async Task AFailureIsReportedAtOnceAsync()
    {
        PrChecksResponse failed = Checks(PrChecksVerdict.Failed);
        this.MockPrChecksServiceRead(failed);

        PrChecksWaitResponse response = await this.WaitAsync();

        AssertOutcome(
            response: response,
            outcome: PrChecksWaitOutcome.Failed,
            reads: 1,
            waitedSeconds: 0,
            lastRead: failed
        );
        Assert.Empty(this._clock.Delays);
    }

    [Theory]
    [InlineData(PrChecksVerdict.Pending, PrChecksWaitOutcome.Pending)]
    [InlineData(PrChecksVerdict.NoChecks, PrChecksWaitOutcome.NoChecks)]
    public async Task ChecksStillWaitingAtTheLimitAreReportedAsTheyStandAsync(
        PrChecksVerdict verdict,
        PrChecksWaitOutcome outcome
    )
    {
        PrChecksResponse waiting = Checks(verdict);
        this.MockPrChecksServiceRead(waiting);

        PrChecksWaitResponse response = await this.WaitAsync();

        AssertOutcome(response: response, outcome: outcome, reads: 5, waitedSeconds: 60, lastRead: waiting);
        Assert.Equal(expected: [PollInterval, PollInterval, PollInterval, PollInterval], actual: this._clock.Delays);
    }

    [Fact]
    public async Task APassNotYetConfirmedAtTheLimitIsPendingAsync()
    {
        PrChecksResponse unconfirmed = Checks(PrChecksVerdict.Passed);
        PrChecksResponse pending = Checks(PrChecksVerdict.Pending);
        this.MockPrChecksServiceRead(pending, pending, pending, pending, unconfirmed);

        PrChecksWaitResponse response = await this.WaitAsync();

        AssertOutcome(
            response: response,
            outcome: PrChecksWaitOutcome.Pending,
            reads: 5,
            waitedSeconds: 60,
            lastRead: unconfirmed
        );
    }

    [Fact]
    public async Task TheLastDelayEndsAtTheLimitAsync()
    {
        PrChecksResponse pending = Checks(PrChecksVerdict.Pending);
        this._checks.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PR,
                sha: null,
                branch: null,
                all: false,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                this._clock.Advance(TimeSpan.FromSeconds(20));

                return pending;
            });

        PrChecksWaitResponse response = await this.WaitAsync();

        AssertOutcome(
            response: response,
            outcome: PrChecksWaitOutcome.Pending,
            reads: 3,
            waitedSeconds: 80,
            lastRead: pending
        );
        Assert.Equal(expected: [PollInterval, TimeSpan.FromSeconds(5)], actual: this._clock.Delays);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task TheTimeoutLimitsAreAcceptedAsync(int timeoutMinutes)
    {
        PrChecksResponse waiting = Checks(PrChecksVerdict.Pending);
        this.MockPrChecksServiceRead(waiting);

        PrChecksWaitResponse response = await this.WaitAsync(timeoutMinutes);

        Assert.Equal(expected: PrChecksWaitOutcome.Pending, actual: response.Outcome);
        Assert.Equal(expected: timeoutMinutes * 60, actual: response.WaitedSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    public async Task ATimeoutOutsideTheLimitsIsRefusedWithNothingReadAsync(int timeoutMinutes)
    {
        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.WaitAsync(timeoutMinutes)
        );

        Assert.Equal(expected: "timeoutMinutes", actual: exception.ParameterName);
        Assert.Equal(expected: "it must be a whole number from 1 to 5", actual: exception.Rule);
        await this.AssertNothingReadAsync();
    }

    [Fact]
    public async Task AnInvalidTargetIsRefusedAsTheChecksServiceRefusedItAsync()
    {
        ParameterValidationException failure = ParameterValidationException.For(
            parameterName: "pr",
            rule: "exactly one of pr, sha or branch must be given"
        );
        this.MockPrChecksServiceReadFailsWith(failure);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.WaitAsync()
        );

        Assert.Same(expected: failure, actual: exception);
    }

    [Fact]
    public Task APullRequestThatIsNotFoundStopsTheWaitAsync()
    {
        return this.AssertReadFailureStopsTheWaitAsync(
            new PullRequestNotFoundException("pull request " + REPOSITORY + "#176 was not found")
        );
    }

    [Fact]
    public Task ACommitThatIsNotFoundStopsTheWaitAsync()
    {
        return this.AssertReadFailureStopsTheWaitAsync(
            new CommitNotFoundException("the commit " + SHA + " was not found in " + REPOSITORY)
        );
    }

    [Fact]
    public Task ABranchThatIsNotFoundStopsTheWaitAsync()
    {
        return this.AssertReadFailureStopsTheWaitAsync(
            new BranchNotFoundException("the branch " + BRANCH + " was not found in " + REPOSITORY)
        );
    }

    [Fact]
    public Task ARepositoryThatIsNotFoundStopsTheWaitAsync()
    {
        return this.AssertReadFailureStopsTheWaitAsync(
            new RepositoryNotFoundException(
                "the repository " + REPOSITORY + " was not found, or the caller cannot see it"
            )
        );
    }

    [Fact]
    public Task UnreadableRequiredChecksStopTheWaitAsync()
    {
        return this.AssertReadFailureStopsTheWaitAsync(
            new RequiredChecksUnreadableException("could not read the required checks of " + REPOSITORY)
        );
    }

    [Fact]
    public Task UnreadableChecksStopTheWaitAsync()
    {
        return this.AssertReadFailureStopsTheWaitAsync(
            new PrChecksUnreadableException("could not read the checks of " + REPOSITORY + " commit " + SHA)
        );
    }

    [Fact]
    public async Task AFailedFirstReadNamesThatNothingWasReadAsync()
    {
        BranchNotFoundException failure = new("the branch " + BRANCH + " was not found in " + REPOSITORY);
        this.MockPrChecksServiceReadFailsWith(failure);

        PrChecksWaitFailedException exception = await Assert.ThrowsAsync<PrChecksWaitFailedException>(() =>
            this.WaitAsync()
        );

        Assert.Equal(
            expected: failure.Message + "; the wait stopped after 0 reads over 0 seconds",
            actual: exception.Message
        );
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    [Fact]
    public async Task OtherFailuresAreNotWrappedAsync()
    {
        InvalidOperationException failure = new("upstream detail");
        this.MockPrChecksServiceReadFailsWith(failure);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            this.WaitAsync()
        );

        Assert.Same(expected: failure, actual: exception);
    }

    [Fact]
    public async Task EveryReadIsReportedAsProgressAsync()
    {
        PrChecksResponse pending = Checks(PrChecksVerdict.Pending);
        PrChecksResponse failed = Checks(PrChecksVerdict.Failed);
        this.MockPrChecksServiceRead(pending, failed);

        await this.WaitAsync();

        Received.InOrder(() =>
        {
            this._progress.Report(pending);
            this._progress.Report(failed);
        });
    }

    [Fact]
    public async Task TheTargetIsPassedToTheChecksServiceAsync()
    {
        PrChecksResponse failed = Checks(PrChecksVerdict.Failed);
        this._checks.ReadAsync(
                repository: REPOSITORY,
                pullRequest: null,
                sha: SHA,
                branch: BRANCH,
                all: true,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(failed);

        PrChecksWaitResponse response = await this._service.WaitAsync(
            repository: REPOSITORY,
            pullRequest: null,
            sha: SHA,
            branch: BRANCH,
            all: true,
            timeoutMinutes: ONE_MINUTE,
            progress: this._progress,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: failed, actual: response.LastRead);
    }

    private async Task AssertReadFailureStopsTheWaitAsync(Exception failure)
    {
        this._checks.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PR,
                sha: null,
                branch: null,
                all: false,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(_ => Checks(PrChecksVerdict.Pending), _ => Checks(PrChecksVerdict.Pending), _ => throw failure);

        PrChecksWaitFailedException exception = await Assert.ThrowsAsync<PrChecksWaitFailedException>(() =>
            this.WaitAsync()
        );

        Assert.Equal(
            expected: failure.Message + "; the wait stopped after 2 reads over 30 seconds",
            actual: exception.Message
        );
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    private Task AssertNothingReadAsync()
    {
        return this
            ._checks.DidNotReceiveWithAnyArgs()
            .ReadAsync(
                repository: default!,
                pullRequest: default,
                sha: default,
                branch: default,
                all: default,
                cancellationToken: default
            )
            .AsTask();
    }

    private void MockPrChecksServiceRead(PrChecksResponse first, params PrChecksResponse[] rest)
    {
        this._checks.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PR,
                sha: null,
                branch: null,
                all: false,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(first, rest);
    }

    private void MockPrChecksServiceReadFailsWith(Exception failure)
    {
        this._checks.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PR,
                sha: null,
                branch: null,
                all: false,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<PrChecksWaitResponse> WaitAsync(int timeoutMinutes = ONE_MINUTE)
    {
        return this
            ._service.WaitAsync(
                repository: REPOSITORY,
                pullRequest: PR,
                sha: null,
                branch: null,
                all: false,
                timeoutMinutes: timeoutMinutes,
                progress: this._progress,
                cancellationToken: this.CancellationToken()
            )
            .AsTask();
    }

    private static void AssertOutcome(
        PrChecksWaitResponse response,
        PrChecksWaitOutcome outcome,
        int reads,
        int waitedSeconds,
        PrChecksResponse lastRead
    )
    {
        Assert.Equal(expected: outcome, actual: response.Outcome);
        Assert.Equal(expected: reads, actual: response.Reads);
        Assert.Equal(expected: waitedSeconds, actual: response.WaitedSeconds);
        Assert.Same(expected: lastRead, actual: response.LastRead);
    }

    private static PrChecksResponse Checks(PrChecksVerdict verdict, string sha = SHA)
    {
        return new(
            Repo: REPOSITORY,
            Sha: sha,
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
