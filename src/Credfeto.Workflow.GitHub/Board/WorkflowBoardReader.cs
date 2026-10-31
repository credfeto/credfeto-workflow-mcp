using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Board.LoggingExtensions;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;
using Microsoft.Extensions.Logging;

namespace Credfeto.Workflow.GitHub.Board;

public sealed class WorkflowBoardReader : IWorkflowBoardReader
{
    // GitHub returns at most 100 items per page, so this allows 10,000 board items before giving up
    private const int MAX_PAGES = 100;

    private const string ISSUE_TYPE = "ISSUE";
    private const string PULL_REQUEST_TYPE = "PULL_REQUEST";

    private const string ISSUE_NOT_FOUND = "Could not resolve to an Issue with the number";
    private const string PULL_REQUEST_NOT_FOUND = "Could not resolve to a PullRequest with the number";

    private const string FIELD_VALUES =
        "legacy:fieldValueByName(name:\"Workflow Status\"){... on ProjectV2ItemFieldSingleSelectValue{name}} status:fieldValueByName(name:\"Status\"){... on ProjectV2ItemFieldSingleSelectValue{name}}";

    private const string ISSUE_STATUS_QUERY =
        $"query($o:String!,$r:String!,$n:Int!){{repository(owner:$o,name:$r){{item:issue(number:$n){{projectItems(first:100){{nodes{{project{{id}} {FIELD_VALUES}}} pageInfo{{hasNextPage endCursor}}}}}}}}}}";

    private const string PULL_REQUEST_STATUS_QUERY =
        $"query($o:String!,$r:String!,$n:Int!){{repository(owner:$o,name:$r){{item:pullRequest(number:$n){{projectItems(first:100){{nodes{{project{{id}} {FIELD_VALUES}}} pageInfo{{hasNextPage endCursor}}}}}}}}}}";

    private const string BOARD_ITEMS_QUERY =
        $"query($project:ID!,$c:String){{node(id:$project){{... on ProjectV2{{items(first:100,after:$c){{nodes{{type content{{... on Issue{{number repository{{nameWithOwner}}}} ... on PullRequest{{number repository{{nameWithOwner}}}}}} {FIELD_VALUES}}} pageInfo{{hasNextPage endCursor}}}}}}}}}}";

    private static readonly BoardItemStatus NotOnBoard = new(State: BoardItemState.NOT_ON_BOARD, Name: null);
    private static readonly BoardItemStatus Unset = new(State: BoardItemState.UNSET, Name: null);

    private readonly IGitHubGraphQlClient _graphQlClient;
    private readonly ILogger<WorkflowBoardReader> _logger;

    public WorkflowBoardReader(IGitHubGraphQlClient graphQlClient, ILogger<WorkflowBoardReader> logger)
    {
        this._graphQlClient = graphQlClient;
        this._logger = logger;
    }

    public async ValueTask<BoardItemStatus> ReadStatusAsync(
        string owner,
        string repo,
        WorkItemKind kind,
        int number,
        WorkflowProject project,
        WorkflowStatusField field,
        CancellationToken cancellationToken
    )
    {
        string repository = GitHubNames.Repository(value: $"{owner}/{repo}", parameterName: nameof(repo));

        if (number <= 0)
        {
            throw ParameterValidationException.For(parameterName: nameof(number), rule: "must be above 0");
        }

        string reference = $"{repository}#{number}";

        try
        {
            return await this.ReadDirectAsync(
                owner: owner,
                repo: repo,
                kind: kind,
                number: number,
                project: project,
                field: field,
                cancellationToken: cancellationToken
            );
        }
        catch (GitHubGraphQlException exception) when (IsNotFound(exception))
        {
            return NotOnBoard;
        }
        catch (Exception exception)
            when (exception is GitHubGraphQlException or GitHubInvalidResponseException or HttpRequestException)
        {
            this._logger.LogDirectReadFailed(
                reference: reference,
                reason: UpstreamText.ReplaceLineBreaking(exception.Message)
            );
        }

        return await this.ReadFromListingAsync(
            repository: repository,
            reference: reference,
            kind: kind,
            number: number,
            project: project,
            field: field,
            cancellationToken: cancellationToken
        );
    }

    private static bool IsNotFound(GitHubGraphQlException exception)
    {
        return exception.Errors.Any(error =>
            error.Contains(value: ISSUE_NOT_FOUND, comparisonType: StringComparison.Ordinal)
            || error.Contains(value: PULL_REQUEST_NOT_FOUND, comparisonType: StringComparison.Ordinal)
        );
    }

    private async ValueTask<BoardItemStatus> ReadDirectAsync(
        string owner,
        string repo,
        WorkItemKind kind,
        int number,
        WorkflowProject project,
        WorkflowStatusField field,
        CancellationToken cancellationToken
    )
    {
        StatusQueryData data = await this._graphQlClient.ExecuteAsync(
            query: kind == WorkItemKind.PULL_REQUEST ? PULL_REQUEST_STATUS_QUERY : ISSUE_STATUS_QUERY,
            variables: new StatusQueryVariables(Owner: owner, Name: repo, Number: number),
            variablesTypeInfo: GitHubJsonContext.Default.StatusQueryVariables,
            dataTypeInfo: GitHubJsonContext.Default.StatusQueryData,
            cancellationToken: cancellationToken
        );

        StatusProjectItemsConnection connection =
            data.Repository?.Item?.ProjectItems
            ?? throw new GitHubInvalidResponseException("the project items of the work item were missing");

        StatusProjectItemNode? match = (connection.Nodes ?? [])
            .OfType<StatusProjectItemNode>()
            .FirstOrDefault(node => StringComparer.Ordinal.Equals(x: node.Project?.Id, y: project.ProjectNodeId));

        if (match is not null)
        {
            return ToStatus(legacy: match.Legacy, status: match.Status, field: field);
        }

        // Only a complete list of the work item's project items proves it is not on the board
        if (connection.PageInfo is not { HasNextPage: false })
        {
            throw new GitHubInvalidResponseException("the project items of the work item were incomplete");
        }

        return NotOnBoard;
    }

    private async ValueTask<BoardItemStatus> ReadFromListingAsync(
        string repository,
        string reference,
        WorkItemKind kind,
        int number,
        WorkflowProject project,
        WorkflowStatusField field,
        CancellationToken cancellationToken
    )
    {
        string message =
            $"could not read the status of {reference}: both the direct query and the listing of project {project.Number} failed";

        try
        {
            return await this.ReadAllPagesAsync(
                repository: repository,
                message: message,
                kind: kind,
                number: number,
                project: project,
                field: field,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception is GitHubGraphQlException or GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new BoardReadFailedException(message: message, innerException: exception);
        }
    }

    private async ValueTask<BoardItemStatus> ReadAllPagesAsync(
        string repository,
        string message,
        WorkItemKind kind,
        int number,
        WorkflowProject project,
        WorkflowStatusField field,
        CancellationToken cancellationToken
    )
    {
        string type = kind == WorkItemKind.PULL_REQUEST ? PULL_REQUEST_TYPE : ISSUE_TYPE;
        HashSet<string> seenCursors = [with(StringComparer.Ordinal)];
        string? after = null;

        for (int page = 0; page < MAX_PAGES; ++page)
        {
            BoardItemsQueryData data = await this._graphQlClient.ExecuteAsync(
                query: BOARD_ITEMS_QUERY,
                variables: new BoardItemsQueryVariables(Project: project.ProjectNodeId, After: after),
                variablesTypeInfo: GitHubJsonContext.Default.BoardItemsQueryVariables,
                dataTypeInfo: GitHubJsonContext.Default.BoardItemsQueryData,
                cancellationToken: cancellationToken
            );

            BoardItemsConnection connection = data.Node?.Items ?? throw new BoardReadFailedException(message);
            ProjectsPageInfo pageInfo = connection.PageInfo ?? throw new BoardReadFailedException(message);

            BoardItemNode? match = (connection.Nodes ?? [])
                .OfType<BoardItemNode>()
                .FirstOrDefault(node => IsWorkItem(node: node, type: type, repository: repository, number: number));

            if (match is not null)
            {
                return ToStatus(legacy: match.Legacy, status: match.Status, field: field);
            }

            if (!pageInfo.HasNextPage)
            {
                return NotOnBoard;
            }

            // The cursor comes from the upstream, so a missing or repeated one must fail rather than loop forever
            after = pageInfo.EndCursor;

            if (after is null || !seenCursors.Add(after))
            {
                throw new BoardReadFailedException(message);
            }
        }

        throw new BoardReadFailedException(message);
    }

    private static bool IsWorkItem(BoardItemNode node, string type, string repository, int number)
    {
        return StringComparer.Ordinal.Equals(x: node.Type, y: type)
            && node.Content?.Number == number
            && StringComparer.OrdinalIgnoreCase.Equals(x: node.Content.Repository?.NameWithOwner, y: repository);
    }

    // The legacy "Workflow Status" field wins over the board's built-in Status, which only counts when it holds one
    // of the workflow states
    private static BoardItemStatus ToStatus(BoardFieldValue? legacy, BoardFieldValue? status, WorkflowStatusField field)
    {
        string? legacyName = legacy?.Name;

        if (!string.IsNullOrEmpty(legacyName))
        {
            return new(State: BoardItemState.STATE, Name: legacyName);
        }

        string? statusName = status?.Name;

        return statusName is not null && field.OptionIds.ContainsKey(statusName)
            ? new(State: BoardItemState.STATE, Name: statusName)
            : Unset;
    }
}
