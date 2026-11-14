using System;
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

namespace Credfeto.Workflow.GitHub.Dependabot;

public sealed class DependabotAlertsService : IDependabotAlertsService
{
    // GitHub answers with these 403 messages when alerts are turned off for the repository, and when the caller's
    // token lacks the permission to read them, which a caller fixes in different places
    private const string ALERTS_DISABLED_MESSAGE = "Dependabot alerts are disabled";
    private const string NOT_ACCESSIBLE_MESSAGE = "Resource not accessible";

    private readonly IGitHubRestClient _restClient;

    public DependabotAlertsService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public ValueTask<DependabotAlertsResult> ListAsync(
        string repository,
        string? state,
        string? severity,
        int limit,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        string validState = DependabotAlertsQuery.State(value: state, parameterName: nameof(state));
        string? validSeverity = DependabotAlertsQuery.Severity(value: severity, parameterName: nameof(severity));
        int validLimit = DependabotAlertsQuery.Limit(value: limit, parameterName: nameof(limit));

        return this.ReadAsync(
            validRepository: validRepository,
            relativeUri: DependabotAlertsQuery.RequestPath(
                validRepository: validRepository,
                validState: validState,
                validSeverity: validSeverity,
                validLimit: validLimit
            ),
            validLimit: validLimit,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<DependabotAlertsResult> ReadAsync(
        string validRepository,
        string relativeUri,
        int validLimit,
        CancellationToken cancellationToken
    )
    {
        try
        {
            DependabotAlertResponse?[] alerts = await this._restClient.GetAsync(
                relativeUri: relativeUri,
                responseTypeInfo: GitHubJsonContext.Default.DependabotAlertResponseArray,
                cancellationToken: cancellationToken
            );

            // GitHub lists the newest alerts first, so they are kept in its order
            return new(
                Repository: validRepository,
                Alerts: [.. alerts.Take(validLimit).Select(DependabotAlertsProjector.Project)]
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new DependabotAlertsNotFoundException(
                message: $"the repository {validRepository} was not found, or the caller cannot see it",
                innerException: exception
            );
        }
        catch (HttpRequestException exception)
            when (IsForbiddenWith(exception: exception, message: ALERTS_DISABLED_MESSAGE))
        {
            throw new DependabotAlertsDisabledException(
                message: $"Dependabot alerts are disabled for {validRepository}",
                innerException: exception
            );
        }
        catch (HttpRequestException exception)
            when (IsForbiddenWith(exception: exception, message: NOT_ACCESSIBLE_MESSAGE))
        {
            throw new DependabotAlertsPermissionException(
                message: $"the caller's token is not allowed to read the Dependabot alerts of {validRepository}; it needs read access to Dependabot alerts, or the security_events scope for a classic token",
                innerException: exception
            );
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new DependabotAlertsFailedException(
                message: $"could not list the Dependabot alerts of {validRepository}",
                innerException: exception
            );
        }
    }

    private static bool IsForbiddenWith(HttpRequestException exception, string message)
    {
        return exception.StatusCode == HttpStatusCode.Forbidden
            && GitHubHttpFailure.UpstreamMessage(exception) is { } upstreamMessage
            && upstreamMessage.Contains(value: message, comparisonType: StringComparison.Ordinal);
    }
}
