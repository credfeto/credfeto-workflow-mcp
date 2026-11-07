using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Runs;

public sealed class RunListService : IRunListService
{
    private readonly IGitHubRestClient _restClient;

    public RunListService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public ValueTask<RunListResponse> ListAsync(
        string repository,
        string? branch,
        string? sha,
        string? workflow,
        int limit,
        CancellationToken cancellationToken
    )
    {
        RunListRequest request = new(
            Repository: GitHubNames.Repository(value: repository, parameterName: nameof(repository)),
            Filter: ValidFilter(branch: branch, sha: sha),
            Workflow: workflow is null ? null : RunQuery.Workflow(value: workflow, parameterName: nameof(workflow)),
            Limit: RunQuery.Limit(value: limit, parameterName: nameof(limit))
        );

        return this.ReadAsync(request: request, cancellationToken: cancellationToken);
    }

    private static RunFilter ValidFilter(string? branch, string? sha)
    {
        bool hasBranch = branch is not null;
        bool hasSha = sha is not null;

        if (hasBranch == hasSha)
        {
            throw ParameterValidationException.For(
                parameterName: nameof(branch),
                rule: "exactly one of branch or sha must be given"
            );
        }

        if (sha is not null)
        {
            string validSha = GitHubNames.CommitSha(value: sha, parameterName: nameof(sha));

            return new(Branch: null, Sha: validSha, Query: "head_sha=" + validSha);
        }

        string validBranch = GitHubNames.Branch(value: branch!, parameterName: nameof(branch));

        return new(Branch: validBranch, Sha: null, Query: "branch=" + Uri.EscapeDataString(validBranch));
    }

    private async ValueTask<RunListResponse> ReadAsync(RunListRequest request, CancellationToken cancellationToken)
    {
        try
        {
            string runsPath = await this.RunsPathAsync(request: request, cancellationToken: cancellationToken);

            // No event filter is set, so runs of every trigger are listed
            WorkflowRunsPageResponse page = await this._restClient.GetAsync(
                relativeUri: $"{runsPath}?{request.Filter.Query}&per_page={request.Limit}",
                responseTypeInfo: GitHubJsonContext.Default.WorkflowRunsPageResponse,
                cancellationToken: cancellationToken
            );

            IReadOnlyList<WorkflowRunResponse> runs = ValidRuns(page: page, filter: request.Filter);

            return new(
                Repository: request.Repository,
                Runs:
                [
                    .. runs.OrderByDescending(run => run.CreatedAt)
                        .ThenByDescending(run => run.Id)
                        .Take(request.Limit)
                        .Select(Project),
                ]
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // A missing repository is a failure of the read, not a run or workflow that does not exist
            throw new RunListFailedException(
                message: $"the repository {request.Repository} was not found, or the caller cannot see it",
                innerException: exception
            );
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new RunListFailedException(
                message: $"could not list the workflow runs of {request.Repository}",
                innerException: exception
            );
        }
    }

    private async ValueTask<string> RunsPathAsync(RunListRequest request, CancellationToken cancellationToken)
    {
        if (request.Workflow is null)
        {
            return $"repos/{request.Repository}/actions/runs";
        }

        long workflowId = await this.ResolveWorkflowIdAsync(request: request, cancellationToken: cancellationToken);

        return $"repos/{request.Repository}/actions/workflows/{workflowId}/runs";
    }

    // A disabled workflow is still in the list, so its runs can be read
    private async ValueTask<long> ResolveWorkflowIdAsync(RunListRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkflowResponse?> workflows = await this._restClient.GetAllPagesAsync(
            relativeUri: $"repos/{request.Repository}/actions/workflows?per_page=100",
            pageTypeInfo: GitHubJsonContext.Default.WorkflowsPageResponse,
            items: page => page.Workflows,
            cancellationToken: cancellationToken
        );

        WorkflowResponse[] valid =
        [
            .. workflows.Select(workflow =>
                workflow is { Id: not null }
                    ? workflow
                    : throw new GitHubInvalidResponseException("GitHub returned a workflow with no id")
            ),
        ];

        string wanted = request.Workflow!;
        WorkflowResponse[] byFile = [.. valid.Where(workflow => IsFile(workflow: workflow, file: wanted))];
        WorkflowResponse[] matches =
            byFile.Length != 0
                ? byFile
                : [.. valid.Where(workflow => StringComparer.Ordinal.Equals(x: workflow.Name, y: wanted))];

        return matches switch
        {
            [{ Id: { } id }] => id,
            [] => throw new WorkflowNotFoundException($"the workflow {wanted} was not found in {request.Repository}"),
            _ => throw new RunListFailedException(
                $"more than one workflow in {request.Repository} matches {wanted}; give its file name"
            ),
        };
    }

    private static bool IsFile(WorkflowResponse workflow, string file)
    {
        string? path = workflow.Path;

        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        int separator = path.AsSpan().LastIndexOf('/');

        return StringComparer.Ordinal.Equals(x: path[(separator + 1)..], y: file);
    }

    // Every run is checked against the request before any is returned, so a filter GitHub ignored cannot surface runs
    // of another branch or commit
    private static IReadOnlyList<WorkflowRunResponse> ValidRuns(WorkflowRunsPageResponse page, RunFilter filter)
    {
        IReadOnlyList<WorkflowRunResponse?> runs =
            page.WorkflowRuns ?? throw new GitHubInvalidResponseException("GitHub returned no list of workflow runs");

        return
        [
            .. runs.Select(run =>
                run is { Id: not null } && filter.Matches(run)
                    ? run
                    : throw new GitHubInvalidResponseException(
                        "GitHub returned a workflow run that does not match the request"
                    )
            ),
        ];
    }

    private static RunListEntry Project(WorkflowRunResponse run)
    {
        return new(
            Id: run.Id!.Value,
            Workflow: RunText.Field(run.Name),
            Status: RunText.Field(run.Status),
            Conclusion: RunText.Field(run.Conclusion),
            HeadSha: RunText.Field(run.HeadSha),
            Event: RunText.Field(run.Event),
            CreatedAt: RunText.Timestamp(run.CreatedAt)
        );
    }

    [DebuggerDisplay("{Repository} {Filter.Query} {Workflow} {Limit}")]
    private readonly record struct RunListRequest(string Repository, RunFilter Filter, string? Workflow, int Limit);

    [DebuggerDisplay("{Query}")]
    private readonly record struct RunFilter(string? Branch, string? Sha, string Query)
    {
        public bool Matches(WorkflowRunResponse run)
        {
            return this.Sha is { } sha
                ? StringComparer.OrdinalIgnoreCase.Equals(x: run.HeadSha, y: sha)
                : StringComparer.Ordinal.Equals(x: run.HeadBranch, y: this.Branch);
        }
    }
}
