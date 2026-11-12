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
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Labels;

public sealed class LabelListService : ILabelListService
{
    private const int MAX_LABELS = 1000;

    private readonly IGitHubRestClient _restClient;

    public LabelListService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public ValueTask<LabelListResponse> ListAsync(string repository, CancellationToken cancellationToken)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        return this.ReadAsync(validRepository: validRepository, cancellationToken: cancellationToken);
    }

    private async ValueTask<LabelListResponse> ReadAsync(string validRepository, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<IssueLabelResponse?> labels = await this._restClient.GetAllPagesAsync(
                relativeUri: $"repos/{validRepository}/labels?per_page=100",
                pageTypeInfo: GitHubJsonContext.Default.IssueLabelResponseArray,
                cancellationToken: cancellationToken
            );

            // Listing only part of the labels would silently hide the rest, so too many is refused
            if (labels.Count > MAX_LABELS)
            {
                throw new LabelListFailedException($"{validRepository} has more than {MAX_LABELS} labels to list");
            }

            return new(Repository: validRepository, Labels: [.. labels.Select(LabelName)]);
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
            throw new LabelListFailedException(
                message: $"could not list the labels of {validRepository}",
                innerException: exception
            );
        }
    }

    private static string LabelName(IssueLabelResponse? label)
    {
        return label?.Name is { } name && !string.IsNullOrWhiteSpace(name)
            ? name
            : throw new GitHubInvalidResponseException("GitHub returned a label without a name");
    }
}
