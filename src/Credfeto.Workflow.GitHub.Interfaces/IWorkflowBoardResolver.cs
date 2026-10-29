using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IWorkflowBoardResolver
{
    ValueTask<WorkflowProject> ResolveAsync(string repository, CancellationToken cancellationToken);

    void Invalidate(string repository);
}
