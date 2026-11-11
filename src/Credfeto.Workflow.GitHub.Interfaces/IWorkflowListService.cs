using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IWorkflowListService
{
    ValueTask<WorkflowListResponse> ListAsync(string repository, CancellationToken cancellationToken);
}
