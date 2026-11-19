using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IAuthStatusService
{
    ValueTask<AuthStatusResponse> ReadAsync(bool rateLimit, CancellationToken cancellationToken);
}
