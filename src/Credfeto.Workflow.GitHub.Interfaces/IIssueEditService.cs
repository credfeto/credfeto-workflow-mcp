using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IIssueEditService
{
    ValueTask<IssueEditResult> EditAsync(
        string repository,
        int issue,
        string? title,
        string? body,
        string? priority,
        bool assignSelf,
        CancellationToken cancellationToken
    );
}
