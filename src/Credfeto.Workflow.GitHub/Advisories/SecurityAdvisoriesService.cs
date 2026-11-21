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

namespace Credfeto.Workflow.GitHub.Advisories;

public sealed class SecurityAdvisoriesService : ISecurityAdvisoriesService
{
    private const int MAX_ADVISORIES = 1000;

    private readonly IGitHubRestClient _restClient;

    public SecurityAdvisoriesService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public ValueTask<SecurityAdvisoriesResult> ListAsync(string repository, CancellationToken cancellationToken)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        return this.ReadAsync(validRepository: validRepository, cancellationToken: cancellationToken);
    }

    private async ValueTask<SecurityAdvisoriesResult> ReadAsync(
        string validRepository,
        CancellationToken cancellationToken
    )
    {
        try
        {
            IReadOnlyList<SecurityAdvisoryResponse?> advisories = await this._restClient.GetAllPagesAsync(
                relativeUri: $"repos/{validRepository}/security-advisories?per_page=100",
                pageTypeInfo: GitHubJsonContext.Default.SecurityAdvisoryResponseArray,
                cancellationToken: cancellationToken
            );

            // Listing only part of the advisories would silently hide the rest, so too many is refused
            if (advisories.Count > MAX_ADVISORIES)
            {
                throw new SecurityAdvisoriesFailedException(
                    $"{validRepository} has more than {MAX_ADVISORIES} security advisories, so none is listed"
                );
            }

            // GitHub lists the newest advisories first, so they are kept in its order
            return new(
                Repository: validRepository,
                Advisories: [.. advisories.Select(SecurityAdvisoriesProjector.Project)]
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new SecurityAdvisoriesNotFoundException(
                message: $"the repository {validRepository} was not found, or the caller cannot see it",
                innerException: exception
            );
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new SecurityAdvisoriesFailedException(
                message: $"could not list the security advisories of {validRepository}",
                innerException: exception
            );
        }
    }
}
