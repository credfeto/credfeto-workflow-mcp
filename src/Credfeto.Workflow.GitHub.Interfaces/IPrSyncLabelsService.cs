using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IPrSyncLabelsService
{
    ValueTask<PrSyncLabelsResult> SyncAsync(
        string repository,
        int pullRequest,
        bool assignSelf,
        CancellationToken cancellationToken
    );
}
