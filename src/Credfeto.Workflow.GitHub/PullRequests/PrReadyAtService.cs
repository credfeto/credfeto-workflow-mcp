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

public sealed class PrReadyAtService : IPrReadyAtService
{
    public const string SOURCE_READY_FOR_REVIEW = "ready-for-review";

    public const string SOURCE_CREATED = "created";

    private const string READY_FOR_REVIEW_EVENT = "ready_for_review";

    private readonly IGitHubRestClient _restClient;

    public PrReadyAtService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public async ValueTask<PrReadyAtResponse> ReadAsync(
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
            return await this.ReadCheckedAsync(
                repository: validRepository,
                pullRequest: pullRequest,
                number: number,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // An issue shares the pull request number space, and GitHub answers 404 for one asked for as a pull request
            throw new PullRequestNotFoundException(
                message: $"pull request {reference} was not found",
                innerException: exception
            );
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new PrReadyAtFailedException(
                message: $"could not read when pull request {reference} became ready for review",
                innerException: exception
            );
        }
    }

    private async ValueTask<PrReadyAtResponse> ReadCheckedAsync(
        string repository,
        int pullRequest,
        string number,
        CancellationToken cancellationToken
    )
    {
        PullRequestCreatedResponse pull = await this._restClient.GetAsync(
            relativeUri: $"repos/{repository}/pulls/{number}",
            responseTypeInfo: GitHubJsonContext.Default.PullRequestCreatedResponse,
            cancellationToken: cancellationToken
        );

        if (pull.Number != pullRequest)
        {
            throw new GitHubInvalidResponseException("GitHub returned another pull request");
        }

        string createdAt = Timestamp(pull.CreatedAt);

        IReadOnlyList<TimelineEventResponse?> events = await this._restClient.GetAllPagesAsync(
            relativeUri: $"repos/{repository}/issues/{number}/timeline?per_page=100",
            pageTypeInfo: GitHubJsonContext.Default.TimelineEventResponseArray,
            cancellationToken: cancellationToken
        );

        // The timeline is oldest first, so the last ready event is the most recent time it was marked ready
        TimelineEventResponse? ready = events.LastOrDefault(item =>
            StringComparer.Ordinal.Equals(x: item?.Event, y: READY_FOR_REVIEW_EVENT)
        );

        return ready is null
            ? new(ReadyAt: createdAt, Source: SOURCE_CREATED)
            : new(ReadyAt: Timestamp(ready.CreatedAt), Source: SOURCE_READY_FOR_REVIEW);
    }

    private static string Timestamp(string? value)
    {
        return value is not null && IssueCommentsProjector.TryParseTimestamp(value: value, out _)
            ? value
            : throw new GitHubInvalidResponseException("GitHub returned a time that could not be read");
    }
}
