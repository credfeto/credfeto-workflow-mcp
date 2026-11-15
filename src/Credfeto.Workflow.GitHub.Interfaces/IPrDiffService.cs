using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IPrDiffService
{
    ValueTask<PrDiffResult> DiffAsync(
        string repository,
        int pullRequest,
        string? mode,
        CancellationToken cancellationToken
    );
}
