using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Repositories;

public sealed class RepoCollaboratorsService : IRepoCollaboratorsService
{
    private const int MAX_COLLABORATORS = 1000;

    private const string NO_ROLE = "-";

    private readonly IGitHubRestClient _restClient;

    public RepoCollaboratorsService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public ValueTask<RepoCollaboratorsResponse> ListAsync(string repository, CancellationToken cancellationToken)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        return this.ReadAsync(validRepository: validRepository, cancellationToken: cancellationToken);
    }

    private async ValueTask<RepoCollaboratorsResponse> ReadAsync(
        string validRepository,
        CancellationToken cancellationToken
    )
    {
        try
        {
            IReadOnlyList<RepoCollaboratorResponse?> collaborators = await this._restClient.GetAllPagesAsync(
                relativeUri: $"repos/{validRepository}/collaborators?per_page=100",
                pageTypeInfo: GitHubJsonContext.Default.RepoCollaboratorResponseArray,
                cancellationToken: cancellationToken
            );

            // Listing only part of the collaborators would silently hide the rest, so too many is refused
            if (collaborators.Count > MAX_COLLABORATORS)
            {
                throw new RepoCollaboratorsFailedException(
                    $"{validRepository} has more than {MAX_COLLABORATORS} collaborators, so none is listed"
                );
            }

            return new(
                Repository: validRepository,
                Collaborators:
                [
                    .. collaborators
                        .Select(Collaborator)
                        .OrderBy(keySelector: c => c.Login, comparer: StringComparer.Ordinal),
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
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            // GitHub only lists collaborators to callers with push access; an empty list here would be a lie
            throw new RepoCollaboratorsFailedException(
                message: $"the caller lacks push access to {validRepository}, which GitHub requires to list its collaborators",
                innerException: exception
            );
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new RepoCollaboratorsFailedException(
                message: $"could not list the collaborators of {validRepository}",
                innerException: exception
            );
        }
    }

    private static RepoCollaborator Collaborator(RepoCollaboratorResponse? collaborator)
    {
        // The login is not echoed in the error, since it failed validation and could carry anything
        if (collaborator?.Login is not { } login || !GitHubNames.IsLogin(login))
        {
            throw new GitHubInvalidResponseException("GitHub returned a collaborator without a valid login");
        }

        return new(Login: login, Role: Role(collaborator.RoleName));
    }

    private static string Role(string? roleName)
    {
        return string.IsNullOrEmpty(roleName) ? NO_ROLE : UpstreamText.ReplaceLineBreaking(roleName);
    }
}
