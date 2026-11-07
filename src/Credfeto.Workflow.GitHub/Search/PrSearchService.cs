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
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Search;

public sealed class PrSearchService : IPrSearchService
{
    private readonly IGitHubRestClient _restClient;

    public PrSearchService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public ValueTask<PrSearchResult> SearchAsync(
        string repository,
        string text,
        PrSearchState state,
        int limit,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        int validLimit = IssueSearchQuery.Limit(value: limit, parameterName: nameof(limit));
        string words = IssueSearchText.Words(text: text, parameterName: nameof(text));
        string query = PrSearchQuery.Build(repository: validRepository, words: words, state: state);

        return this.ReadAsync(
            validRepository: validRepository,
            query: query,
            validLimit: validLimit,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<PrSearchResult> ReadAsync(
        string validRepository,
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
            PrSearchResponse response = await this._restClient.GetAsync(
                relativeUri: $"search/issues?q={Uri.EscapeDataString(query)}&per_page={perPage}",
                responseTypeInfo: GitHubJsonContext.Default.PrSearchResponse,
                cancellationToken: cancellationToken
            );

            PrSearchItemResponse?[] items =
                response.Items ?? throw new GitHubInvalidResponseException("GitHub returned a search without items");

            return new(
                Repository: validRepository,
                PullRequests: [.. items.Take(validLimit).Select(PrSearchProjector.Project)],
                Truncated: items.Length > validLimit || response.TotalCount > validLimit
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
            throw new PrSearchFailedException(
                message: $"could not search the pull requests of {validRepository}",
                innerException: exception
            );
        }
    }
}
