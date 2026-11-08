using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IPrStatusService
{
    ValueTask<PrStatusResponse> ReadAsync(string repository, int pullRequest, CancellationToken cancellationToken);
}
