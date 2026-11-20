using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;

namespace Credfeto.Workflow.GitHub.Board;

public sealed class WorkflowStatusCheckService : IWorkflowStatusCheckService
{
    // Reading a status only needs the workflow state names to recognise one held in the board's built-in Status
    // field, so a read-only check makes no field lookup and never needs the real field or option ids
    private static readonly WorkflowStatusField StateNames = new(
        FieldId: "workflow-states",
        FieldName: "Status",
        OptionIds: WorkflowStates.All.ToDictionary(
            keySelector: state => state,
            elementSelector: state => state,
            comparer: StringComparer.Ordinal
        )
    );

    private readonly IWorkflowBoardReader _boardReader;
    private readonly IWorkflowBoardResolver _boardResolver;

    public WorkflowStatusCheckService(IWorkflowBoardResolver boardResolver, IWorkflowBoardReader boardReader)
    {
        this._boardResolver = boardResolver;
        this._boardReader = boardReader;
    }

    public ValueTask<WorkflowStatusCheckResult> CheckAsync(
        string repository,
        int? pullRequest,
        int? issue,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        BoardWorkItem workItem = BoardWorkItem.Validate(pullRequest: pullRequest, issue: issue);

        return this.ReadAsync(repository: validRepository, workItem: workItem, cancellationToken: cancellationToken);
    }

    public ValueTask<WorkflowBoardItems> ListAsync(string repository, CancellationToken cancellationToken)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        return this.ListBoardAsync(repository: validRepository, cancellationToken: cancellationToken);
    }

    private async ValueTask<WorkflowBoardItems> ListBoardAsync(string repository, CancellationToken cancellationToken)
    {
        WorkflowProject project = await this._boardResolver.ResolveAsync(
            repository: repository,
            cancellationToken: cancellationToken
        );

        IReadOnlyList<BoardListedItem> listed = await this._boardReader.ListItemsAsync(
            project: project,
            field: StateNames,
            cancellationToken: cancellationToken
        );

        IReadOnlyList<WorkflowBoardItem> items = [.. listed.Select(ToBoardItem)];

        return new(Repository: repository, Total: items.Count, Items: items);
    }

    private static WorkflowBoardItem ToBoardItem(BoardListedItem item)
    {
        return new(
            Kind: item.Kind == WorkItemKind.PULL_REQUEST
                ? WorkflowBoardItemKind.PullRequest
                : WorkflowBoardItemKind.Issue,
            Repository: item.Repository,
            Number: item.Number,
            State: item.Status.State == BoardItemState.STATE ? item.Status.Name : null
        );
    }

    private async ValueTask<WorkflowStatusCheckResult> ReadAsync(
        string repository,
        BoardWorkItem workItem,
        CancellationToken cancellationToken
    )
    {
        WorkflowProject project = await this._boardResolver.ResolveAsync(
            repository: repository,
            cancellationToken: cancellationToken
        );

        // GitHubNames.Repository guarantees exactly one separator between a non-empty owner and name
        int separator = repository.IndexOf(value: '/', comparisonType: StringComparison.Ordinal);

        BoardItemStatus status = await this._boardReader.ReadStatusAsync(
            owner: repository[..separator],
            repo: repository[(separator + 1)..],
            kind: workItem.Kind,
            number: workItem.Number,
            project: project,
            field: StateNames,
            cancellationToken: cancellationToken
        );

        return status.State switch
        {
            BoardItemState.STATE => new(Outcome: WorkflowStatusCheckOutcome.Found, State: status.Name, Message: null),
            BoardItemState.UNSET => new(Outcome: WorkflowStatusCheckOutcome.Unset, State: null, Message: null),
            _ => new(
                Outcome: WorkflowStatusCheckOutcome.NotOnBoard,
                State: null,
                Message: $"{repository}#{workItem.Number} is not on the Workflow board"
            ),
        };
    }
}
