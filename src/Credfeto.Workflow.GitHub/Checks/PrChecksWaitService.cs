using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;

namespace Credfeto.Workflow.GitHub.Checks;

public sealed class PrChecksWaitService : IPrChecksWaitService
{
    public const int DEFAULT_TIMEOUT_MINUTES = 3;

    // The limit keeps one call well inside the tool call timeout of MCP clients; a caller that needs longer calls again
    private const int MAX_TIMEOUT_MINUTES = 5;

    private const string TIMEOUT_RULE = "it must be a whole number from 1 to 5";

    // Checks take minutes, so reading more often only spends the API budget
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly IPrChecksService _prChecksService;
    private readonly TimeProvider _timeProvider;

    public PrChecksWaitService(IPrChecksService prChecksService, TimeProvider timeProvider)
    {
        this._prChecksService = prChecksService;
        this._timeProvider = timeProvider;
    }

    public ValueTask<PrChecksWaitResponse> WaitAsync(
        string repository,
        int? pullRequest,
        string? sha,
        string? branch,
        bool all,
        int timeoutMinutes,
        IProgress<PrChecksResponse> progress,
        CancellationToken cancellationToken
    )
    {
        TimeSpan limit = ValidTimeout(timeoutMinutes);

        return this.WaitForChecksAsync(
            target: new(Repository: repository, PullRequest: pullRequest, Sha: sha, Branch: branch, All: all),
            limit: limit,
            progress: progress,
            cancellationToken: cancellationToken
        );
    }

    // The timeout is validated before anything is read, so a refused call never reads the checks
    private static TimeSpan ValidTimeout(int timeoutMinutes)
    {
        return timeoutMinutes is >= 1 and <= MAX_TIMEOUT_MINUTES
            ? TimeSpan.FromMinutes(timeoutMinutes)
            : throw ParameterValidationException.For(parameterName: nameof(timeoutMinutes), rule: TIMEOUT_RULE);
    }

    private async ValueTask<PrChecksWaitResponse> WaitForChecksAsync(
        WaitTarget target,
        TimeSpan limit,
        IProgress<PrChecksResponse> progress,
        CancellationToken cancellationToken
    )
    {
        long started = this._timeProvider.GetTimestamp();
        int reads = 0;

        // A pass only counts once the next read of the same head passes too, as checks can still be starting
        string? passedSha = null;

        while (true)
        {
            PrChecksResponse checks = await this.ReadAsync(
                target: target,
                reads: reads,
                started: started,
                cancellationToken: cancellationToken
            );
            ++reads;
            progress.Report(checks);

            if (Settled(checks: checks, passedSha: passedSha) is { } settled)
            {
                return this.Result(outcome: settled, reads: reads, started: started, checks: checks);
            }

            passedSha = checks.Verdict == PrChecksVerdict.Passed ? checks.Sha : null;

            TimeSpan remaining = limit - this._timeProvider.GetElapsedTime(started);

            if (remaining <= TimeSpan.Zero)
            {
                return this.Result(outcome: AtLimit(checks), reads: reads, started: started, checks: checks);
            }

            await Task.Delay(
                delay: remaining < PollInterval ? remaining : PollInterval,
                timeProvider: this._timeProvider,
                cancellationToken: cancellationToken
            );
        }
    }

    private static PrChecksWaitOutcome? Settled(PrChecksResponse checks, string? passedSha)
    {
        return checks.Verdict switch
        {
            PrChecksVerdict.Failed => PrChecksWaitOutcome.Failed,
            PrChecksVerdict.Passed when StringComparer.Ordinal.Equals(x: passedSha, y: checks.Sha) =>
                PrChecksWaitOutcome.Passed,
            _ => null,
        };
    }

    // A pass still waiting for its confirming read is not yet a pass, so it is reported as pending
    private static PrChecksWaitOutcome AtLimit(PrChecksResponse checks)
    {
        return checks.Verdict == PrChecksVerdict.NoChecks ? PrChecksWaitOutcome.NoChecks : PrChecksWaitOutcome.Pending;
    }

    private async ValueTask<PrChecksResponse> ReadAsync(
        WaitTarget target,
        int reads,
        long started,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await this._prChecksService.ReadAsync(
                repository: target.Repository,
                pullRequest: target.PullRequest,
                sha: target.Sha,
                branch: target.Branch,
                all: target.All,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is PullRequestNotFoundException
                        or CommitNotFoundException
                        or BranchNotFoundException
                        or RepositoryNotFoundException
                        or RequiredChecksUnreadableException
                        or PrChecksUnreadableException
            )
        {
            // Every one of these messages is fixed text or validated caller input, so it is kept, with how far the
            // wait got so the caller knows what was already seen
            throw new PrChecksWaitFailedException(
                message: $"{exception.Message}; the wait stopped after {reads.ToString(CultureInfo.InvariantCulture)} reads over {this.WaitedSeconds(started).ToString(CultureInfo.InvariantCulture)} seconds",
                innerException: exception
            );
        }
    }

    private PrChecksWaitResponse Result(PrChecksWaitOutcome outcome, int reads, long started, PrChecksResponse checks)
    {
        return new(Outcome: outcome, Reads: reads, WaitedSeconds: this.WaitedSeconds(started), LastRead: checks);
    }

    private int WaitedSeconds(long started)
    {
        return (int)this._timeProvider.GetElapsedTime(started).TotalSeconds;
    }

    [DebuggerDisplay("{Repository}")]
    private readonly record struct WaitTarget(
        string Repository,
        int? PullRequest,
        string? Sha,
        string? Branch,
        bool All
    );
}
