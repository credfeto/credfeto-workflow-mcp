using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IRunListService
{
    // Exactly one of branch and sha names the commit the runs were triggered for
    ValueTask<RunListResponse> ListAsync(
        string repository,
        string? branch,
        string? sha,
        string? workflow,
        int limit,
        CancellationToken cancellationToken
    );
}
