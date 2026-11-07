using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;

namespace Credfeto.Workflow.GitHub.Runs;

public sealed class RunViewService : IRunViewService
{
    private readonly IGitHubRestClient _restClient;

    public RunViewService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public ValueTask<RunViewResponse> ViewAsync(string repository, string run, CancellationToken cancellationToken)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        long runId = RunQuery.Id(value: run, parameterName: nameof(run));

        return this.ReadAsync(repository: validRepository, run: runId, cancellationToken: cancellationToken);
    }

    private async ValueTask<RunViewResponse> ReadAsync(string repository, long run, CancellationToken cancellationToken)
    {
        try
        {
            WorkflowRunResponse response = await RunReader.ReadRunAsync(
                restClient: this._restClient,
                repository: repository,
                run: run,
                cancellationToken: cancellationToken
            );

            IReadOnlyList<WorkflowJobResponse> jobs = await RunReader.ReadJobsAsync(
                restClient: this._restClient,
                repository: repository,
                run: response,
                cancellationToken: cancellationToken
            );

            return new(
                Repository: repository,
                Id: run,
                Status: RunText.Field(response.Status),
                Conclusion: RunText.Field(response.Conclusion),
                HeadSha: RunText.Field(response.HeadSha),
                HeadBranch: RunText.Field(response.HeadBranch),
                Workflow: RunText.Field(response.Name),
                Event: RunText.Field(response.Event),
                Attempt: response.RunAttempt!.Value,
                CreatedAt: RunText.Timestamp(response.CreatedAt),
                Jobs: [.. jobs.Select(Project)]
            );
        }
        catch (RepositoryNotFoundException exception)
        {
            // A missing repository is a failure of the read, not a run that does not exist
            throw new RunViewFailedException(message: exception.Message, innerException: exception);
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new RunViewFailedException(
                message: $"could not read the run {run} of {repository}",
                innerException: exception
            );
        }
    }

    private static RunJobEntry Project(WorkflowJobResponse job)
    {
        return new(
            Id: job.Id!.Value,
            Name: RunText.Field(job.Name),
            Status: RunText.Field(job.Status),
            Conclusion: RunText.Field(job.Conclusion)
        );
    }
}
