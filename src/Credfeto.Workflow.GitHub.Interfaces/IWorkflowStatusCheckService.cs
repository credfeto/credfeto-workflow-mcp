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
}
