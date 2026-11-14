using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IDependabotAlertsService
{
    ValueTask<DependabotAlertsResult> ListAsync(
        string repository,
        string? state,
        string? severity,
        int limit,
        CancellationToken cancellationToken
    );
}
