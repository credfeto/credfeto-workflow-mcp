using System;
using System.Collections.Generic;
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
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.PullRequests;

public sealed class PrAutoMergeAtService : IPrAutoMergeAtService
{
    public const string NEVER_ENABLED = "-";

    private const string AUTO_MERGE_ENABLED_EVENT = "auto_merge_enabled";

    private readonly IGitHubRestClient _restClient;

    public PrAutoMergeAtService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public async ValueTask<PrAutoMergeAtResponse> ReadAsync(
        string repository,
        int pullRequest,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (pullRequest <= 0)
        {
            throw ParameterValidationException.For(parameterName: nameof(pullRequest), rule: "must be above 0");
        }

        string reference = $"{validRepository}#{pullRequest}";
        string number = pullRequest.ToString(CultureInfo.InvariantCulture);

        try
        {
            string enabledAt = await this.ReadEnabledAtAsync(
                repository: validRepository,
                pullRequest: pullRequest,
                number: number,
                cancellationToken: cancellationToken
            );

            return new(Pr: pullRequest, EnabledAt: enabledAt);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new PullRequestNotFoundException(
                message: $"pull request {reference} was not found",
                innerException: exception
            );
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new PrAutoMergeAtFailedException(
                message: $"could not read when auto-merge was enabled on pull request {reference}",
                innerException: exception
            );
        }
    }

    private async ValueTask<string> ReadEnabledAtAsync(
        string repository,
        int pullRequest,
        string number,
        CancellationToken cancellationToken
    )
    {
        // An issue shares the pull request number space and has a timeline too, so the pull request is read first
        // for GitHub to answer 404 when the number is not a pull request
        PullRequestCreatedResponse pull = await this._restClient.GetAsync(
            relativeUri: $"repos/{repository}/pulls/{number}",
            responseTypeInfo: GitHubJsonContext.Default.PullRequestCreatedResponse,
            cancellationToken: cancellationToken
        );

        if (pull.Number != pullRequest)
        {
            throw new GitHubInvalidResponseException("GitHub returned another pull request");
        }

        IReadOnlyList<TimelineEventResponse?> events = await this._restClient.GetAllPagesAsync(
            relativeUri: $"repos/{repository}/issues/{number}/timeline?per_page=100",
            pageTypeInfo: GitHubJsonContext.Default.TimelineEventResponseArray,
            cancellationToken: cancellationToken
        );

        // The timeline is oldest first, so the last enabled event is the most recent time auto-merge was enabled
        TimelineEventResponse? enabled = events.LastOrDefault(item =>
            StringComparer.Ordinal.Equals(x: item?.Event, y: AUTO_MERGE_ENABLED_EVENT)
        );

        return enabled is null ? NEVER_ENABLED : Timestamp(enabled.CreatedAt);
    }

    private static string Timestamp(string? value)
    {
        return value is not null && IssueCommentsProjector.TryParseTimestamp(value: value, out _)
            ? value
            : throw new GitHubInvalidResponseException("GitHub returned a time that could not be read");
    }
}
