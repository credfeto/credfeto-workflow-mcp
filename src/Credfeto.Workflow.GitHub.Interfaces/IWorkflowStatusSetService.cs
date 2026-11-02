using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IWorkflowStatusSetService
{
    // Exactly one of pullRequest and issue names the work item, which is added to the board first when it is not on it
    ValueTask<WorkflowStatusSetResponse> SetAsync(
        string repository,
        int? pullRequest,
        int? issue,
        string status,
        CancellationToken cancellationToken
    );
}
