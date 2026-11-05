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

namespace Credfeto.Workflow.GitHub.Issues;

public sealed class IssueListService : IIssueListService
{
    private const int MAX_ISSUES = 1000;

    private readonly IGitHubRestClient _restClient;

    public IssueListService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public ValueTask<IssueListResponse> ListOpenIssuesAsync(string repository, CancellationToken cancellationToken)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        return this.ReadAsync(validRepository: validRepository, cancellationToken: cancellationToken);
    }

    private async ValueTask<IssueListResponse> ReadAsync(string validRepository, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<IssueListItemResponse?> pages = await this._restClient.GetAllPagesAsync(
                relativeUri: $"repos/{validRepository}/issues?state=open&per_page=100",
                pageTypeInfo: GitHubJsonContext.Default.IssueListItemResponseArray,
                cancellationToken: cancellationToken
            );

            IReadOnlyList<IssueListItemResponse> issues =
            [
                .. pages.Select(IssueListProjector.Required).Where(IssueListProjector.IsIssue),
            ];

            // Listing only part of the open issues would silently hide the rest, so too many is refused
            if (issues.Count > MAX_ISSUES)
            {
                throw new IssueListFailedException($"{validRepository} has more than {MAX_ISSUES} open issues to list");
            }

            return new(
                Repository: validRepository,
                Issues:
                [
                    .. issues
                        .Select(IssueListProjector.Project)
                        .OrderBy(entry => StandardLabels.PriorityRank(entry.Priority))
                        .ThenBy(entry => entry.Number),
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
            throw new IssueListFailedException(
                message: $"could not list the open issues of {validRepository}",
                innerException: exception
            );
        }
    }
}
