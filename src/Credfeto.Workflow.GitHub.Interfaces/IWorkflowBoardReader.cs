using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IWorkflowBoardReader
{
    ValueTask<BoardItemStatus> ReadStatusAsync(
        string owner,
        string repo,
        WorkItemKind kind,
        int number,
        WorkflowProject project,
        WorkflowStatusField field,
        CancellationToken cancellationToken
    );
}
