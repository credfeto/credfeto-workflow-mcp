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

    private readonly IGitHubGraphQlClient _graphQlClient;
    private readonly IGitHubRestClient _restClient;

    public PullRequestListService(IGitHubRestClient restClient, IGitHubGraphQlClient graphQlClient)
    {
        this._restClient = restClient;
        this._graphQlClient = graphQlClient;
    }

    public ValueTask<PullRequestListResponse> ListAsync(
        string repository,
        int? issue,
        string? head,
        IReadOnlyList<string>? labels,
        bool closingIssues,
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
            closingIssues: closingIssues,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<PullRequestListResponse> ReadAsync(
        string validRepository,
        int? issue,
        string? validHead,
        IReadOnlyList<string> validLabels,
        bool closingIssues,
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

            IReadOnlyList<PullRequestListEntry> entries = Entries(
                onHead: onHead,
                issue: issue,
                validLabels: validLabels
            );

            return new(
                Repository: validRepository,
                PullRequests: closingIssues
                    ? await this.WithClosingIssuesAsync(
                        validRepository: validRepository,
                        entries: entries,
                        cancellationToken: cancellationToken
                    )
                    : entries
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new RepositoryNotFoundException(
                message: $"the repository {validRepository} was not found, or the caller cannot see it",
                innerException: exception
            );
        }
        catch (Exception exception)
            when (exception is GitHubInvalidResponseException or GitHubGraphQlException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new PullRequestListFailedException(
                message: $"could not list the pull requests of {validRepository}",
                innerException: exception
            );
        }
    }

    // Only the listed pull requests are read, by number, so one closed since the REST listing still resolves
    private async ValueTask<IReadOnlyList<PullRequestListEntry>> WithClosingIssuesAsync(
        string validRepository,
        IReadOnlyList<PullRequestListEntry> entries,
        CancellationToken cancellationToken
    )
    {
        // GitHubNames.Repository guarantees exactly one separator between a non-empty owner and name
        int separator = validRepository.IndexOf('/', StringComparison.Ordinal);
        PullRequestListClosingQueryVariables variables = new(
            Owner: validRepository[..separator],
            Name: validRepository[(separator + 1)..]
        );

        List<PullRequestListEntry> result = [];

        foreach (PullRequestListEntry[] batch in entries.Chunk(PullRequestListClosingQuery.BATCH_SIZE))
        {
            PullRequestListClosingQueryData data = await this._graphQlClient.ExecuteAsync(
                query: PullRequestListClosingQuery.Build(batch.Select(entry => entry.Number)),
                variables: variables,
                variablesTypeInfo: GitHubJsonContext.Default.PullRequestListClosingQueryVariables,
                dataTypeInfo: GitHubJsonContext.Default.PullRequestListClosingQueryData,
                cancellationToken: cancellationToken
            );

            IReadOnlyDictionary<string, PullRequestListClosingNode?> pullRequests = data.Repository
                ?? throw IncompleteClosingIssues();

            result.AddRange(
                batch.Select(entry =>
                    entry with
                    {
                        ClosingIssues = ClosingIssues(pullRequests: pullRequests, number: entry.Number),
                    }
                )
            );
        }

        return result;
    }

    private static IReadOnlyList<PullRequestListEntry> Entries(
        IReadOnlyList<PullRequestListItemResponse> onHead,
        int? issue,
        IReadOnlyList<string> validLabels
    )
    {
        return
        [
            .. onHead
                .Where(item => PullRequestListProjector.HasEveryLabel(item: item, labels: validLabels))
                .Select(item => PullRequestListProjector.Project(item: item, issue: issue))
                .OrderBy(entry => entry.Number),
        ];
    }

    private static IReadOnlyList<ClosingIssueReference> ClosingIssues(
        IReadOnlyDictionary<string, PullRequestListClosingNode?> pullRequests,
        int number
    )
    {
        return pullRequests.TryGetValue(
            key: PullRequestListClosingQuery.Alias(number),
            out PullRequestListClosingNode? node
        )
            ? ClosingReferenceProjector.TryProject(node?.ClosingIssuesReferences) ?? throw IncompleteClosingIssues()
            : throw IncompleteClosingIssues();
    }

    private static GitHubInvalidResponseException IncompleteClosingIssues()
    {
        return new("GitHub returned incomplete closing issue references");
    }

    private static IReadOnlyList<string> Labels(IReadOnlyList<string>? labels)
    {
        return [.. (labels ?? []).Select(label => GitHubNames.ListLabel(value: label, parameterName: nameof(labels)))];
    }
}
