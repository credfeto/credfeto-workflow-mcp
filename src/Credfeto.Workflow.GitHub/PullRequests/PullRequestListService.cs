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

namespace Credfeto.Workflow.GitHub.PullRequests;

public sealed class PullRequestListService : IPullRequestListService
{
    private const int MAX_PULL_REQUESTS = 1000;

    private readonly IGitHubRestClient _restClient;

    public PullRequestListService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public ValueTask<PullRequestListResponse> ListAsync(
        string repository,
        int? issue,
        string? head,
        IReadOnlyList<string>? labels,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (issue <= 0)
        {
            throw ParameterValidationException.For(parameterName: nameof(issue), rule: "must be above 0");
        }

        string? validHead = head is null ? null : GitHubNames.Branch(value: head, parameterName: nameof(head));
        IReadOnlyList<string> validLabels = Labels(labels);

        return this.ReadAsync(
            validRepository: validRepository,
            issue: issue,
            validHead: validHead,
            validLabels: validLabels,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<PullRequestListResponse> ReadAsync(
        string validRepository,
        int? issue,
        string? validHead,
        IReadOnlyList<string> validLabels,
        CancellationToken cancellationToken
    )
    {
        try
        {
            IReadOnlyList<PullRequestListItemResponse?> pages = await this._restClient.GetAllPagesAsync(
                relativeUri: $"repos/{validRepository}/pulls?state=open&per_page=100",
                pageTypeInfo: GitHubJsonContext.Default.PullRequestListItemResponseArray,
                cancellationToken: cancellationToken
            );

            IReadOnlyList<PullRequestListItemResponse> onHead =
            [
                .. pages
                    .Select(item =>
                        item ?? throw new GitHubInvalidResponseException("GitHub returned an empty pull request")
                    )
                    .Where(item => PullRequestListProjector.HasHead(item: item, head: validHead)),
            ];

            // Listing only part of the open pull requests would silently hide the rest, so too many is refused
            if (onHead.Count > MAX_PULL_REQUESTS)
            {
                throw new PullRequestListFailedException(
                    $"{validRepository} has more than {MAX_PULL_REQUESTS} open pull requests to list"
                );
            }

            return new(
                Repository: validRepository,
                PullRequests:
                [
                    .. onHead
                        .Where(item => PullRequestListProjector.HasEveryLabel(item: item, labels: validLabels))
                        .Select(item => PullRequestListProjector.Project(item: item, issue: issue))
                        .OrderBy(entry => entry.Number),
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
            throw new PullRequestListFailedException(
                message: $"could not list the pull requests of {validRepository}",
                innerException: exception
            );
        }
    }

    private static IReadOnlyList<string> Labels(IReadOnlyList<string>? labels)
    {
        return [.. (labels ?? []).Select(label => GitHubNames.ListLabel(value: label, parameterName: nameof(labels)))];
    }
}
