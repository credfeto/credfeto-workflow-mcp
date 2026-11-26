using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.PullRequests;

// The board column a multi-step pull request tool moves the pull request to, resolved before anything is written so a
// repository with no Workflow board, or no such column, fails before the pull request is touched
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{Option.OptionId}")]
internal readonly record struct PullRequestBoardTarget(WorkflowProject Project, ResolvedStatusOption Option)
{
    public static async ValueTask<PullRequestBoardTarget> ResolveAsync(
        IWorkflowBoardResolver boardResolver,
        IWorkflowStatusResolver statusResolver,
        string repository,
        string state,
        CancellationToken cancellationToken
    )
    {
        WorkflowProject project = await boardResolver.ResolveAsync(
            repository: repository,
            cancellationToken: cancellationToken
        );

        ResolvedStatusOption option = await statusResolver.ResolveAsync(
            repository: repository,
            project: project,
            state: state,
            cancellationToken: cancellationToken
        );

        return new(Project: project, Option: option);
    }

    public ValueTask SetAsync(
        IBoardWriter boardWriter,
        string owner,
        string name,
        int number,
        CancellationToken cancellationToken
    )
    {
        return boardWriter.SetItemStatusAsync(
            owner: owner,
            repo: name,
            kind: WorkItemKind.PULL_REQUEST,
            number: number,
            project: this.Project,
            fieldId: this.Option.FieldId,
            fieldName: this.Option.FieldName,
            optionId: this.Option.OptionId,
            cancellationToken: cancellationToken
        );
    }
}
