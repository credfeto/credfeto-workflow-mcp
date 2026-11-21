using System;
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

public sealed class AdvisoryViewService : IAdvisoryViewService
{
    private readonly IGitHubRestClient _restClient;

    public AdvisoryViewService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    // The id is validated before anything is read, so a malformed one is reported as such and never sent to GitHub
    public ValueTask<AdvisoryViewResult> ViewAsync(string ghsa, CancellationToken cancellationToken)
    {
        string validGhsa = GitHubNames.GhsaId(value: ghsa, parameterName: nameof(ghsa));

        return this.ReadAsync(ghsa: validGhsa, cancellationToken: cancellationToken);
    }

    private async ValueTask<AdvisoryViewResult> ReadAsync(string ghsa, CancellationToken cancellationToken)
    {
        try
        {
            GlobalAdvisoryResponse advisory = await this._restClient.GetAsync(
                relativeUri: $"advisories/{ghsa}",
                responseTypeInfo: GitHubJsonContext.Default.GlobalAdvisoryResponse,
                cancellationToken: cancellationToken
            );

            return AdvisoryViewProjector.Project(ghsa: ghsa, advisory: advisory);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new AdvisoryNotFoundException(
                message: $"the security advisory {ghsa} was not found",
                innerException: exception
            );
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new AdvisoryViewFailedException(
                message: $"could not read the security advisory {ghsa}",
                innerException: exception
            );
        }
    }
}
