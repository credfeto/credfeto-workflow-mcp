using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IWorkflowStatusCheckService
{
    // Exactly one of pullRequest and issue names the work item; one that is not on the board is a result, not a failure
    ValueTask<WorkflowStatusCheckResult> CheckAsync(
        string repository,
        int? pullRequest,
        int? issue,
        CancellationToken cancellationToken
    );

    // Every issue and pull request on the repository's Workflow board, with its status judged as CheckAsync does
    ValueTask<WorkflowBoardItems> ListAsync(string repository, CancellationToken cancellationToken);
}
