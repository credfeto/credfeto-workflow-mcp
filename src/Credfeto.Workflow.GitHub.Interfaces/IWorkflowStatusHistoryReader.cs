using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IWorkflowStatusHistoryReader
{
    ValueTask<WorkflowStatusHistoryResult> ReadAsync(
        string repository,
        int? issue,
        int? pr,
        CancellationToken cancellationToken
    );
}
