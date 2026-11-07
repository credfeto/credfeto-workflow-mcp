using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IPrEditService
{
    ValueTask<PrEditResult> EditAsync(
        string repository,
        int pullRequest,
        string? title,
        string? body,
        bool assignSelf,
        CancellationToken cancellationToken
    );
}
