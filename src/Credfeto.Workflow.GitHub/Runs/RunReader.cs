using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Runs;

internal static class RunReader
{
    private const int MAX_JOBS = 1000;

    public static async ValueTask<WorkflowRunResponse> ReadRunAsync(
        IGitHubRestClient restClient,
        string repository,
        long run,
        CancellationToken cancellationToken
    )
    {
        WorkflowRunResponse response;

        try
        {
            response = await restClient.GetAsync(
                relativeUri: $"repos/{repository}/actions/runs/{run}",
                responseTypeInfo: GitHubJsonContext.Default.WorkflowRunResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            await EnsureRepositoryAsync(
                restClient: restClient,
                repository: repository,
                cancellationToken: cancellationToken
            );

            throw new RunNotFoundException(
                message: $"the run {run} was not found in {repository}",
                innerException: exception
            );
        }

        return response is { Id: { } id, RunAttempt: >= 1 } && id == run
            ? response
            : throw new GitHubInvalidResponseException("GitHub returned an invalid workflow run");
    }

    // GitHub answers 404 for a missing repository and for a missing run alike, so the repository is read to tell
    // them apart: a missing repository is a failure of the read rather than a run that does not exist
    public static async ValueTask EnsureRepositoryAsync(
        IGitHubRestClient restClient,
        string repository,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await restClient.GetAsync(
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
    }

    // Only the latest attempt's jobs are read, so a job from an earlier attempt never hides the current result
    public static async ValueTask<IReadOnlyList<WorkflowJobResponse>> ReadJobsAsync(
        IGitHubRestClient restClient,
        string repository,
        WorkflowRunResponse run,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<JobsPage> pages = await restClient.GetAllPagesAsync<WorkflowJobsPageResponse, JobsPage>(
            relativeUri: $"repos/{repository}/actions/runs/{run.Id}/attempts/{run.RunAttempt}/jobs?per_page=100",
            pageTypeInfo: GitHubJsonContext.Default.WorkflowJobsPageResponse,
            items: static page => page.Jobs is null ? null : [new(TotalCount: page.TotalCount, Jobs: page.Jobs)],
            cancellationToken: cancellationToken
        );

        IReadOnlyList<WorkflowJobResponse> jobs =
        [
            .. pages
                .SelectMany(page => page.Jobs)
                .Select(job =>
                    job is { Id: not null }
                        ? job
                        : throw new GitHubInvalidResponseException("GitHub returned a job with no id")
                )
                .OrderBy(job => job.Id),
        ];

        return IsComplete(pages: pages, count: jobs.Count)
            ? jobs
            : throw new GitHubInvalidResponseException("GitHub returned an incomplete list of jobs");
    }

    // A total that changes between pages means the list moved while it was read, so part of it may be missing
    private static bool IsComplete(IReadOnlyList<JobsPage> pages, int count)
    {
        int? total = pages.Count == 0 ? null : pages[0].TotalCount;

        return total is { } expected
            && expected == count
            && expected <= MAX_JOBS
            && pages.All(page => page.TotalCount == expected);
    }

    [DebuggerDisplay("{TotalCount}: {Jobs.Count}")]
    private readonly record struct JobsPage(int? TotalCount, IReadOnlyList<WorkflowJobResponse?> Jobs);
}
