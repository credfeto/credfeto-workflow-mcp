using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IBoardWriter
{
    ValueTask SetItemStatusAsync(
        string owner,
        string repo,
        WorkItemKind kind,
        int number,
        WorkflowProject project,
        string fieldId,
        string fieldName,
        string optionId,
        CancellationToken cancellationToken
    );
}
