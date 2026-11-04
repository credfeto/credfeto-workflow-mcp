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

public sealed class IssueSearchService : IIssueSearchService
{
    private readonly IGitHubRestClient _restClient;

    public IssueSearchService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public ValueTask<IssueSearchResult> SearchAsync(
        string repository,
        string text,
        IssueSearchState state,
        int limit,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        int validLimit = IssueSearchQuery.Limit(value: limit, parameterName: nameof(limit));
        string words = IssueSearchText.Words(text: text, parameterName: nameof(text));
        string query = IssueSearchQuery.Build(repository: validRepository, words: words, state: state);

        return this.ReadAsync(
            validRepository: validRepository,
            query: query,
            validLimit: validLimit,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<IssueSearchResult> ReadAsync(
        string validRepository,
        string query,
        int validLimit,
        CancellationToken cancellationToken
    )
    {
        // One more than the limit is asked for, so a full page shows that further matches were left out
        string perPage = (validLimit + 1).ToString(CultureInfo.InvariantCulture);

        try
        {
            IssueSearchResponse response = await this._restClient.GetAsync(
                relativeUri: $"search/issues?q={Uri.EscapeDataString(query)}&per_page={perPage}",
                responseTypeInfo: GitHubJsonContext.Default.IssueSearchResponse,
                cancellationToken: cancellationToken
            );

            IssueSearchItemResponse?[] items =
                response.Items ?? throw new GitHubInvalidResponseException("GitHub returned a search without items");

            return new(
                Repository: validRepository,
                Issues: [.. items.Take(validLimit).Select(IssueSearchProjector.Project)],
                Truncated: items.Length > validLimit
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
            throw new IssueSearchFailedException(
                message: $"could not search the issues of {validRepository}",
                innerException: exception
            );
        }
    }
}
