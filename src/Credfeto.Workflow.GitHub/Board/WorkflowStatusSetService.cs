using System;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;

namespace Credfeto.Workflow.GitHub.Board;

public sealed class WorkflowStatusSetService : IWorkflowStatusSetService
{
    private readonly IBoardWriter _boardWriter;
    private readonly IWorkflowBoardResolver _boardResolver;
    private readonly IWorkflowStatusResolver _statusResolver;

    public WorkflowStatusSetService(
        IWorkflowBoardResolver boardResolver,
        IWorkflowStatusResolver statusResolver,
        IBoardWriter boardWriter
    )
    {
        this._boardResolver = boardResolver;
        this._statusResolver = statusResolver;
        this._boardWriter = boardWriter;
    }

    public ValueTask<WorkflowStatusSetResponse> SetAsync(
        string repository,
        int? pullRequest,
        int? issue,
        string status,
        CancellationToken cancellationToken
    )
    {
        // Every argument is validated before the board is resolved, so invalid input never costs a GitHub call and is
        // reported as such rather than as a board failure
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        BoardWorkItem workItem = BoardWorkItem.Validate(pullRequest: pullRequest, issue: issue);
        string state = ValidState(status);

        return this.WriteAsync(
            repository: validRepository,
            workItem: workItem,
            state: state,
            cancellationToken: cancellationToken
        );
    }

    private static string ValidState(string status)
    {
        return string.IsNullOrWhiteSpace(status)
            ? throw ParameterValidationException.For(parameterName: nameof(status), rule: "must not be empty")
            : WorkflowStates.Canonicalise(status);
    }

    private async ValueTask<WorkflowStatusSetResponse> WriteAsync(
        string repository,
        BoardWorkItem workItem,
        string state,
        CancellationToken cancellationToken
    )
    {
        WorkflowProject project = await this._boardResolver.ResolveAsync(
            repository: repository,
            cancellationToken: cancellationToken
        );

        ResolvedStatusOption option = await this._statusResolver.ResolveAsync(
            repository: repository,
            project: project,
            state: state,
            cancellationToken: cancellationToken
        );

        // GitHubNames.Repository guarantees exactly one separator between a non-empty owner and name
        int separator = repository.IndexOf(value: '/', comparisonType: StringComparison.Ordinal);

        // A write that returns is the success signal: the value is never read back, as the writer already fails on
        // any response that does not confirm the change
        await this._boardWriter.SetItemStatusAsync(
            owner: repository[..separator],
            repo: repository[(separator + 1)..],
            kind: workItem.Kind,
            number: workItem.Number,
            project: project,
            fieldId: option.FieldId,
            fieldName: option.FieldName,
            optionId: option.OptionId,
            cancellationToken: cancellationToken
        );

        return new(ItemUrl: ItemUrl(repository: repository, workItem: workItem), Status: state);
    }

    private static Uri ItemUrl(string repository, in BoardWorkItem workItem)
    {
        string path = workItem.Kind == WorkItemKind.PULL_REQUEST ? "pull" : "issues";

        return new(uriString: $"https://github.com/{repository}/{path}/{workItem.Number}", uriKind: UriKind.Absolute);
    }
}
