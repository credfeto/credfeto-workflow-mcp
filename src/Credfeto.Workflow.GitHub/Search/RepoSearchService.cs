using System;
using System.Globalization;
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

namespace Credfeto.Workflow.GitHub.Search;

public sealed class RepoSearchService : IRepoSearchService
{
    private readonly IGitHubRestClient _restClient;

    public RepoSearchService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public ValueTask<RepoSearchResult> SearchAsync(
        string owner,
        string text,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken
    )
    {
        string validOwner = GitHubNames.Owner(value: owner, parameterName: nameof(owner));
        int validLimit = IssueSearchQuery.Limit(value: limit, parameterName: nameof(limit));
        string words = IssueSearchText.Words(text: text, parameterName: nameof(text));
        string query = RepoSearchQuery.Build(owner: validOwner, words: words, includeArchived: includeArchived);

        return this.ReadAsync(
            validOwner: validOwner,
            query: query,
            validLimit: validLimit,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<RepoSearchResult> ReadAsync(
        string validOwner,
        string query,
        int validLimit,
        CancellationToken cancellationToken
    )
    {
        // One more than the limit is asked for, so a full page shows that further matches were left out; GitHub caps
        // the page size, so at the cap the reported total count is what shows it instead
        string perPage = Math.Min(val1: validLimit + 1, val2: IssueSearchQuery.MAX_LIMIT)
            .ToString(CultureInfo.InvariantCulture);

        try
        {
            RepoSearchResponse response = await this._restClient.GetAsync(
                relativeUri: $"search/repositories?q={Uri.EscapeDataString(query)}&per_page={perPage}",
                responseTypeInfo: GitHubJsonContext.Default.RepoSearchResponse,
                cancellationToken: cancellationToken
            );

            RepoSearchItemResponse?[] items =
                response.Items ?? throw new GitHubInvalidResponseException("GitHub returned a search without items");

            return new(
                Owner: validOwner,
                Repositories:
                [
                    .. items
                        .Take(validLimit)
                        .Select(RepoSearchProjector.Project)
                        .OrderBy(keySelector: entry => entry.Repository, comparer: StringComparer.Ordinal),
                ],
                Truncated: items.Length > validLimit || response.TotalCount > validLimit
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Search answers an owner it cannot resolve with a validation failure rather than a 404
            throw new OwnerNotFoundException(
                message: $"the owner {validOwner} was not found, or the caller cannot search it",
                innerException: exception
            );
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new RepoSearchFailedException(
                message: $"could not search the repositories of {validOwner}",
                innerException: exception
            );
        }
    }
}
