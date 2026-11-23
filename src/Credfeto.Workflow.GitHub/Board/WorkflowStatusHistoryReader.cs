using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Board;

public sealed class WorkflowStatusHistoryReader : IWorkflowStatusHistoryReader
{
    private const int MAX_STATUS_CHANGES = 1000;

    // GitHub returns at most 100 events per page, so full pages pass the limit before the pages run out
    private const int MAX_PAGES = MAX_STATUS_CHANGES / 100 + 1;

    private const string NONE = "-";

    private const string ISSUE_NOT_FOUND = "Could not resolve to an Issue with the number";
    private const string PULL_REQUEST_NOT_FOUND = "Could not resolve to a PullRequest with the number";

    private const string TIMELINE =
        "timelineItems(first:100,after:$c,itemTypes:[PROJECT_V2_ITEM_STATUS_CHANGED_EVENT]){nodes{... on ProjectV2ItemStatusChangedEvent{createdAt actor{login} previousStatus status project{id}}} pageInfo{hasNextPage endCursor}}";

    private const string ISSUE_QUERY =
        $"query($o:String!,$r:String!,$n:Int!,$c:String){{repository(owner:$o,name:$r){{item:issue(number:$n){{{TIMELINE}}}}}}}";

    private const string PULL_REQUEST_QUERY =
        $"query($o:String!,$r:String!,$n:Int!,$c:String){{repository(owner:$o,name:$r){{item:pullRequest(number:$n){{{TIMELINE}}}}}}}";

    private readonly IWorkflowBoardResolver _boardResolver;
    private readonly IGitHubGraphQlClient _graphQlClient;

    public WorkflowStatusHistoryReader(IGitHubGraphQlClient graphQlClient, IWorkflowBoardResolver boardResolver)
    {
        this._graphQlClient = graphQlClient;
        this._boardResolver = boardResolver;
    }

    public ValueTask<WorkflowStatusHistoryResult> ReadAsync(
        string repository,
        int? issue,
        int? pr,
        CancellationToken cancellationToken
    )
    {
        // Everything is checked before the first request is made
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        (int number, bool isPullRequest) = Target(issue: issue, pr: pr);

        return this.ReadAsync(
            validRepository: validRepository,
            number: number,
            isPullRequest: isPullRequest,
            cancellationToken: cancellationToken
        );
    }

    private static (int Number, bool IsPullRequest) Target(int? issue, int? pr)
    {
        if (issue is null == pr is null)
        {
            throw ParameterValidationException.For(
                parameterName: nameof(pr),
                rule: "exactly one of pr or issue must be given"
            );
        }

        if (pr is { } pullRequest)
        {
            return (Positive(value: pullRequest, parameterName: nameof(pr)), true);
        }

        return (Positive(value: issue.GetValueOrDefault(), parameterName: nameof(issue)), false);
    }

    private static int Positive(int value, string parameterName)
    {
        return value > 0
            ? value
            : throw ParameterValidationException.For(parameterName: parameterName, rule: "must be above 0");
    }

    private async ValueTask<WorkflowStatusHistoryResult> ReadAsync(
        string validRepository,
        int number,
        bool isPullRequest,
        CancellationToken cancellationToken
    )
    {
        // The board exceptions already carry fixed messages, so they reach the caller unchanged
        WorkflowProject project = await this._boardResolver.ResolveAsync(
            repository: validRepository,
            cancellationToken: cancellationToken
        );

        try
        {
            IReadOnlyList<WorkflowStatusHistoryEventResponse> events = await this.ReadAllPagesAsync(
                validRepository: validRepository,
                number: number,
                isPullRequest: isPullRequest,
                cancellationToken: cancellationToken
            );

            return new(
                Repository: validRepository,
                Number: number,
                Changes:
                [
                    .. events
                        .Where(e => StringComparer.Ordinal.Equals(x: e.Project?.Id, y: project.ProjectNodeId))
                        .Select(Project),
                ]
            );
        }
        catch (GitHubGraphQlException exception) when (IsNotFound(exception))
        {
            throw NotFound(repository: validRepository, number: number, isPullRequest: isPullRequest, inner: exception);
        }
        catch (Exception exception)
            when (exception is GitHubGraphQlException or GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new WorkflowStatusHistoryFailedException(
                message: $"could not read the Workflow status history of {Reference(repository: validRepository, number: number)}",
                innerException: exception
            );
        }
    }

    private async ValueTask<IReadOnlyList<WorkflowStatusHistoryEventResponse>> ReadAllPagesAsync(
        string validRepository,
        int number,
        bool isPullRequest,
        CancellationToken cancellationToken
    )
    {
        // GitHubNames.Repository guarantees exactly one separator between a non-empty owner and name
        int separator = validRepository.IndexOf('/', StringComparison.Ordinal);
        string owner = validRepository[..separator];
        string name = validRepository[(separator + 1)..];

        List<WorkflowStatusHistoryEventResponse> events = [];
        HashSet<string> seenCursors = [with(StringComparer.Ordinal)];
        string? after = null;

        for (int page = 0; page < MAX_PAGES; ++page)
        {
            WorkflowStatusHistoryQueryData data = await this._graphQlClient.ExecuteAsync(
                query: isPullRequest ? PULL_REQUEST_QUERY : ISSUE_QUERY,
                variables: new WorkflowStatusHistoryQueryVariables(
                    Owner: owner,
                    Name: name,
                    Number: number,
                    After: after
                ),
                variablesTypeInfo: GitHubJsonContext.Default.WorkflowStatusHistoryQueryVariables,
                dataTypeInfo: GitHubJsonContext.Default.WorkflowStatusHistoryQueryData,
                cancellationToken: cancellationToken
            );

            WorkflowStatusHistoryItemResponse item = data.Repository?.Item
                ?? throw NotFound(
                    repository: validRepository,
                    number: number,
                    isPullRequest: isPullRequest,
                    inner: null
                );
            WorkflowStatusHistoryTimelineResponse timeline = item.TimelineItems ?? throw Incomplete();
            ProjectsPageInfo pageInfo = timeline.PageInfo ?? throw Incomplete();

            events.AddRange((timeline.Nodes ?? []).OfType<WorkflowStatusHistoryEventResponse>());

            // Reading only part of the history would silently hide the rest, so too many is refused before any filter
            if (events.Count > MAX_STATUS_CHANGES)
            {
                throw new WorkflowStatusHistoryFailedException(
                    $"{Reference(repository: validRepository, number: number)} has more than {MAX_STATUS_CHANGES} Workflow status changes to read"
                );
            }

            if (!pageInfo.HasNextPage)
            {
                return events;
            }

            // The cursor comes from the upstream, so a missing or repeated one must fail rather than loop forever
            after = pageInfo.EndCursor;

            if (after is null || !seenCursors.Add(after))
            {
                throw Incomplete();
            }
        }

        throw Incomplete();
    }

    private static bool IsNotFound(GitHubGraphQlException exception)
    {
        return exception.Errors.Any(error =>
            error.Contains(value: ISSUE_NOT_FOUND, comparisonType: StringComparison.Ordinal)
            || error.Contains(value: PULL_REQUEST_NOT_FOUND, comparisonType: StringComparison.Ordinal)
        );
    }

    private static Exception NotFound(string repository, int number, bool isPullRequest, Exception? inner)
    {
        string reference = Reference(repository: repository, number: number);

        return isPullRequest
            ? new PullRequestNotFoundException(
                message: $"the pull request {reference} was not found, or the caller cannot see it",
                innerException: inner
            )
            : new IssueNotFoundException(
                message: $"the issue {reference} was not found, or the caller cannot see it",
                innerException: inner
            );
    }

    private static string Reference(string repository, int number)
    {
        return $"{repository}#{number.ToString(CultureInfo.InvariantCulture)}";
    }

    private static GitHubInvalidResponseException Incomplete()
    {
        return new("GitHub returned an incomplete Workflow status history");
    }

    private static WorkflowStatusHistoryEntry Project(WorkflowStatusHistoryEventResponse change)
    {
        string time =
            change.CreatedAt is { } createdAt && IssueCommentsProjector.TryParseTimestamp(value: createdAt, out _)
                ? createdAt
                : throw new GitHubInvalidResponseException(
                    "GitHub returned a Workflow status change without a valid time"
                );

        return new(
            Time: time,
            Actor: Text(change.Actor?.Login),
            PreviousStatus: Text(change.PreviousStatus),
            Status: Text(change.Status)
        );
    }

    private static string Text(string? value)
    {
        return string.IsNullOrEmpty(value) ? NONE : UpstreamText.ReplaceLineBreaking(value);
    }
}
