using System;
using System.Collections.Generic;
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
using Credfeto.Workflow.GitHub.Runs;

namespace Credfeto.Workflow.GitHub.Workflows;

public sealed class WorkflowListService : IWorkflowListService
{
    private const int MAX_WORKFLOWS = 1000;

    private readonly IGitHubRestClient _restClient;

    public WorkflowListService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public ValueTask<WorkflowListResponse> ListAsync(string repository, CancellationToken cancellationToken)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        return this.ReadAsync(validRepository: validRepository, cancellationToken: cancellationToken);
    }

    private async ValueTask<WorkflowListResponse> ReadAsync(string validRepository, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<WorkflowResponse?> workflows = await this._restClient.GetAllPagesAsync(
                relativeUri: $"repos/{validRepository}/actions/workflows?per_page=100",
                pageTypeInfo: GitHubJsonContext.Default.WorkflowsPageResponse,
                items: page => page.Workflows,
                cancellationToken: cancellationToken
            );

            // Listing only part of the workflows would silently hide the rest, so too many is refused
            if (workflows.Count > MAX_WORKFLOWS)
            {
                throw new WorkflowListRefusedException(
                    $"{validRepository} has more than {MAX_WORKFLOWS} workflows to list"
                );
            }

            return new(
                Repository: validRepository,
                Workflows:
                [
                    .. workflows
                        .Select(WorkflowListProjector.Project)
                        .OrderBy(keySelector: entry => entry.Path, comparer: StringComparer.Ordinal),
                ]
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new RepositoryNotFoundException(
                message: $"the repository {validRepository} was not found, or the caller cannot see it",
                innerException: exception
            );
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new WorkflowListFailedException(
                message: $"could not list the workflows of {validRepository}",
                innerException: exception
            );
        }
    }
}
