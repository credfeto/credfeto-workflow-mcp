using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IWorkflowStatusResolver
{
    ValueTask<ResolvedStatusOption> ResolveAsync(
        string repository,
        WorkflowProject project,
        string state,
        CancellationToken cancellationToken
    );
}
