using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Branches;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Search;

public sealed class CodeSearchService : ICodeSearchService
{
    private const string TEXT_MATCH_MEDIA_TYPE = "application/vnd.github.text-match+json";

    private readonly IGitHubRestClient _restClient;

    public CodeSearchService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public ValueTask<CodeSearchResult> SearchAsync(
        string repository,
        string text,
        int limit,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        int validLimit = IssueSearchQuery.Limit(value: limit, parameterName: nameof(limit));
        string words = IssueSearchText.Words(text: text, parameterName: nameof(text));
        string query = CodeSearchQuery.Build(repository: validRepository, words: words);

        return this.ReadAsync(
            validRepository: validRepository,
            query: query,
            validLimit: validLimit,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<CodeSearchResult> ReadAsync(
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
            // Code search answers a repository it cannot see with a validation failure, not a 404, so the repository
            // is read first to tell a missing repository from a failed search
            await this._restClient.GetAsync(
                relativeUri: $"repos/{validRepository}",
                responseTypeInfo: GitHubJsonContext.Default.RepositoryResponse,
                cancellationToken: cancellationToken
            );

            CodeSearchResponse response = await this._restClient.GetAsync(
                relativeUri: $"search/code?q={Uri.EscapeDataString(query)}&per_page={perPage}",
                mediaType: TEXT_MATCH_MEDIA_TYPE,
                responseTypeInfo: GitHubJsonContext.Default.CodeSearchResponse,
                cancellationToken: cancellationToken
            );

            CodeSearchItemResponse?[] items =
                response.Items ?? throw new GitHubInvalidResponseException("GitHub returned a search without items");

            return new(
                Repository: validRepository,
                Files:
                [
                    .. items
                        .Take(validLimit)
                        .Select(CodeSearchProjector.Project)
                        .OrderBy(keySelector: entry => entry.Path, comparer: StringComparer.Ordinal),
                ],
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
            throw new CodeSearchFailedException(
                message: $"could not search the code of {validRepository}",
                innerException: exception
            );
        }
    }
}
