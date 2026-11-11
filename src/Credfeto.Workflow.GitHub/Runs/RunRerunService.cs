using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Branches;
using Credfeto.Workflow.GitHub.Checks;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Runs;

public sealed class RunRerunService : IRunRerunService
{
    private const int ATTEMPT_LIMIT = 3;
    private const int MAX_PULL_REQUESTS = 1000;

    private const string STATUS_COMPLETED = "completed";
    private const string STATE_OPEN = "open";

    private const string CONCLUSION_SUCCESS = "success";
    private const string CONCLUSION_NEUTRAL = "neutral";
    private const string CONCLUSION_SKIPPED = "skipped";

    private const string RULE_ATTEMPT_LIMIT = "attempt limit";
    private const string RULE_DEFAULT_BRANCH = "default branch";
    private const string RULE_PULL_REQUEST_HEAD = "not an open pull request head";
    private const string RULE_NOTHING_TO_RERUN = "no failed jobs and no required check missing";

    private const string RERUN_FAILED_JOBS = "rerun-failed-jobs";
    private const string RERUN_ALL_JOBS = "rerun";

    private readonly IRequiredChecksReader _requiredChecksReader;
    private readonly IGitHubRestClient _restClient;

    public RunRerunService(IGitHubRestClient restClient, IRequiredChecksReader requiredChecksReader)
    {
        this._restClient = restClient;
        this._requiredChecksReader = requiredChecksReader;
    }

    public ValueTask<RunRerunResult> RerunAsync(string repository, string run, CancellationToken cancellationToken)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        long runId = RunQuery.Id(value: run, parameterName: nameof(run));

        return this.RerunOrFailAsync(repository: validRepository, run: runId, cancellationToken: cancellationToken);
    }

    private async ValueTask<RunRerunResult> RerunOrFailAsync(
        string repository,
        long run,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await this.DecideAsync(repository: repository, run: run, cancellationToken: cancellationToken);
        }
        catch (RunNotFoundException exception)
        {
            return new(
                Outcome: RunRerunOutcome.NotFound,
                Message: exception.Message,
                Run: run,
                Attempt: null,
                Rule: null
            );
        }
        catch (Exception exception)
            when (exception
                    is RepositoryNotFoundException
                        or BranchNotFoundException
                        or RequiredChecksUnreadableException
            )
        {
            // Each of these messages is fixed text or validated input, so it is passed on as the failure
            throw new RunRerunFailedException(message: exception.Message, innerException: exception);
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new RunRerunFailedException(
                message: $"could not rerun the run {run} of {repository}",
                innerException: exception
            );
        }
    }

    // Every rule is checked before the status, so a run that is refused is never reported as pending
    private async ValueTask<RunRerunResult> DecideAsync(
        string repository,
        long run,
        CancellationToken cancellationToken
    )
    {
        WorkflowRunResponse response = await RunReader.ReadRunAsync(
            restClient: this._restClient,
            repository: repository,
            run: run,
            cancellationToken: cancellationToken
        );

        if (response.RunAttempt >= ATTEMPT_LIMIT)
        {
            return Refused(run: run, rule: RULE_ATTEMPT_LIMIT);
        }

        string defaultBranch = await this.ReadDefaultBranchAsync(
            repository: repository,
            cancellationToken: cancellationToken
        );

        // Compared by name alone, so a fork's branch named after the default branch is refused too
        if (StringComparer.Ordinal.Equals(x: response.HeadBranch, y: defaultBranch))
        {
            return Refused(run: run, rule: RULE_DEFAULT_BRANCH);
        }

        if (string.IsNullOrEmpty(response.HeadBranch))
        {
            return Refused(run: run, rule: RULE_PULL_REQUEST_HEAD);
        }

        RunHead head = new(Branch: response.HeadBranch, Sha: ValidHeadSha(response.HeadSha));

        int? pullRequest = await this.FindPullRequestAsync(
            repository: repository,
            run: run,
            head: head,
            cancellationToken: cancellationToken
        );

        if (pullRequest is not { } number)
        {
            return Refused(run: run, rule: RULE_PULL_REQUEST_HEAD);
        }

        if (!StringComparer.Ordinal.Equals(x: response.Status, y: STATUS_COMPLETED))
        {
            return Pending(run);
        }

        return await this.RerunCompletedAsync(
            repository: repository,
            response: response,
            head: head,
            pullRequest: number,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<string> ReadDefaultBranchAsync(string repository, CancellationToken cancellationToken)
    {
        RepositoryResponse response;

        try
        {
            response = await this._restClient.GetAsync(
                relativeUri: $"repos/{repository}",
                responseTypeInfo: GitHubJsonContext.Default.RepositoryResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new RepositoryNotFoundException(
                message: $"the repository {repository} was not found, or the caller cannot see it",
                innerException: exception
            );
        }

        return string.IsNullOrEmpty(response.DefaultBranch)
            ? throw new GitHubInvalidResponseException("GitHub returned a repository without a default branch")
            : response.DefaultBranch;
    }

    // The commit goes into the check and status reads, so one GitHub returned malformed is never trusted
    private static string ValidHeadSha(string? sha)
    {
        try
        {
            return GitHubNames.CommitSha(value: sha ?? string.Empty, parameterName: nameof(sha));
        }
        catch (ParameterValidationException exception)
        {
            throw new GitHubInvalidResponseException(
                message: "GitHub returned an invalid head commit for the run",
                innerException: exception
            );
        }
    }

    // Open pull requests are matched on the head branch name alone, so one from a fork counts too
    private async ValueTask<int?> FindPullRequestAsync(
        string repository,
        long run,
        RunHead head,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<RunPullRequestResponse?> open = await this._restClient.GetAllPagesAsync(
            relativeUri: $"repos/{repository}/pulls?state=open&per_page=100",
            pageTypeInfo: GitHubJsonContext.Default.RunPullRequestResponseArray,
            cancellationToken: cancellationToken
        );

        IReadOnlyList<RunPullRequestResponse> onBranch =
        [
            .. open.Select(item =>
                    item ?? throw new GitHubInvalidResponseException("GitHub returned an empty pull request")
                )
                .Where(item => StringComparer.Ordinal.Equals(x: item.Head?.Ref, y: head.Branch)),
        ];

        // Judging only part of the open pull requests could refuse a run that one of the rest would allow
        if (onBranch.Count > MAX_PULL_REQUESTS)
        {
            throw new RunRerunFailedException(
                $"{repository} has more than {MAX_PULL_REQUESTS} open pull requests on the branch of the run {run}"
            );
        }

        return onBranch
            .Where(item => StringComparer.Ordinal.Equals(x: item.Head?.Sha, y: head.Sha))
            .Min(item =>
                item.Number is > 0
                    ? item.Number
                    : throw new GitHubInvalidResponseException("GitHub returned a pull request without a number")
            );
    }

    private async ValueTask<RunRerunResult> RerunCompletedAsync(
        string repository,
        WorkflowRunResponse response,
        RunHead head,
        int pullRequest,
        CancellationToken cancellationToken
    )
    {
        long run = response.Id!.Value;
        int attempt = response.RunAttempt!.Value + 1;

        if (
            await this.HasFailedJobsAsync(
                repository: repository,
                response: response,
                cancellationToken: cancellationToken
            )
        )
        {
            return await this.RequestRerunAsync(
                repository: repository,
                run: run,
                action: RERUN_FAILED_JOBS,
                attempt: attempt,
                message: $"Rerunning failed jobs of run {run} (attempt {attempt})",
                cancellationToken: cancellationToken
            );
        }

        string? baseBranch = await this.ReadOpenHeadBaseAsync(
            repository: repository,
            pullRequest: pullRequest,
            head: head,
            cancellationToken: cancellationToken
        );

        if (baseBranch is null)
        {
            return Refused(run: run, rule: RULE_PULL_REQUEST_HEAD);
        }

        RequiredCheck? missing = await this.FirstNeverRanAsync(
            repository: repository,
            baseBranch: baseBranch,
            sha: head.Sha,
            cancellationToken: cancellationToken
        );

        if (missing is null)
        {
            return Refused(run: run, rule: RULE_NOTHING_TO_RERUN);
        }

        return await this.RequestRerunAsync(
            repository: repository,
            run: run,
            action: RERUN_ALL_JOBS,
            attempt: attempt,
            message: $"Rerunning all jobs of run {run} (attempt {attempt}): required check {UpstreamText.ReplaceLineBreaking(missing.Name)} never ran",
            cancellationToken: cancellationToken
        );
    }

    // The jobs are only read when the run itself passed, since a run that did not has failed jobs whatever they say
    private async ValueTask<bool> HasFailedJobsAsync(
        string repository,
        WorkflowRunResponse response,
        CancellationToken cancellationToken
    )
    {
        if (!IsPassing(response.Conclusion))
        {
            return true;
        }

        IReadOnlyList<WorkflowJobResponse> jobs = await RunReader.ReadJobsAsync(
            restClient: this._restClient,
            repository: repository,
            run: response,
            cancellationToken: cancellationToken
        );

        return !jobs.All(job => IsPassing(job.Conclusion));
    }

    private static bool IsPassing(string? conclusion)
    {
        return StringComparer.Ordinal.Equals(x: conclusion, y: CONCLUSION_SUCCESS)
            || StringComparer.Ordinal.Equals(x: conclusion, y: CONCLUSION_NEUTRAL)
            || StringComparer.Ordinal.Equals(x: conclusion, y: CONCLUSION_SKIPPED);
    }

    // Read again just before the whole run is rerun, so a pull request closed or pushed to since the rules were
    // checked is refused rather than rerun; null when it no longer has the run's head
    private async ValueTask<string?> ReadOpenHeadBaseAsync(
        string repository,
        int pullRequest,
        RunHead head,
        CancellationToken cancellationToken
    )
    {
        RunPullRequestResponse response;

        try
        {
            response = await this._restClient.GetAsync(
                relativeUri: $"repos/{repository}/pulls/{pullRequest}",
                responseTypeInfo: GitHubJsonContext.Default.RunPullRequestResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        bool isOpenHead =
            StringComparer.Ordinal.Equals(x: response.State, y: STATE_OPEN)
            && StringComparer.Ordinal.Equals(x: response.Head?.Ref, y: head.Branch)
            && StringComparer.Ordinal.Equals(x: response.Head?.Sha, y: head.Sha);

        return isOpenHead ? ValidBaseBranch(response.Base?.Ref) : null;
    }

    private static string ValidBaseBranch(string? branch)
    {
        if (string.IsNullOrEmpty(branch))
        {
            throw new GitHubInvalidResponseException("GitHub returned no branch to read the required checks of");
        }

        try
        {
            return GitHubNames.Branch(value: branch, parameterName: nameof(branch));
        }
        catch (ParameterValidationException exception)
        {
            throw new GitHubInvalidResponseException(
                message: "GitHub returned an invalid branch to read the required checks of",
                innerException: exception
            );
        }
    }

    private async ValueTask<RequiredCheck?> FirstNeverRanAsync(
        string repository,
        string baseBranch,
        string sha,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<RequiredCheck> required = await this._requiredChecksReader.ReadRequiredChecksAsync(
            repository: repository,
            branch: baseBranch,
            cancellationToken: cancellationToken
        );

        if (required.Count == 0)
        {
            return null;
        }

        IReadOnlyList<CheckRunResponse?> runs = await this._restClient.GetAllPagesAsync(
            relativeUri: $"repos/{repository}/commits/{sha}/check-runs?per_page=100&filter=all",
            pageTypeInfo: GitHubJsonContext.Default.CheckRunsPageResponse,
            items: page => page.CheckRuns,
            cancellationToken: cancellationToken
        );

        IReadOnlyList<CommitStatusResponse?> statuses = await this._restClient.GetAllPagesAsync(
            relativeUri: $"repos/{repository}/commits/{sha}/status?per_page=100",
            pageTypeInfo: GitHubJsonContext.Default.CombinedStatusResponse,
            items: page => page.Statuses,
            cancellationToken: cancellationToken
        );

        return PrChecksEvaluator.FirstNeverRan(required: required, runs: runs, statuses: statuses);
    }

    private async ValueTask<RunRerunResult> RequestRerunAsync(
        string repository,
        long run,
        string action,
        int attempt,
        string message,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await this._restClient.PostAsync(
                relativeUri: $"repos/{repository}/actions/runs/{run}/{action}",
                body: new RunRerunRequest(EnableDebugLogging: false),
                requestTypeInfo: GitHubJsonContext.Default.RunRerunRequest,
                responseTypeInfo: GitHubJsonContext.Default.RunRerunResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // Running again is safe: a rerun that did start leaves the run in progress, which is reported as pending
            throw new RunRerunFailedException(
                message: $"the rerun request for the run {run} of {repository} failed, and may still have reached GitHub",
                innerException: exception
            );
        }

        // The attempt is not read back, because GitHub can lag behind the write
        return new(Outcome: RunRerunOutcome.Success, Message: message, Run: run, Attempt: attempt, Rule: null);
    }

    private static RunRerunResult Pending(long run)
    {
        return new(
            Outcome: RunRerunOutcome.Pending,
            Message: $"the run {run} has not completed, and can be rerun once it has",
            Run: run,
            Attempt: null,
            Rule: null
        );
    }

    private static RunRerunResult Refused(long run, string rule)
    {
        return new(
            Outcome: RunRerunOutcome.Refused,
            Message: "rerun refused: " + rule,
            Run: run,
            Attempt: null,
            Rule: rule
        );
    }

    [DebuggerDisplay("{Branch} at {Sha}")]
    private readonly record struct RunHead(string Branch, string Sha);
}
