using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IPrChecksService
{
    // Exactly one of pullRequest, sha and branch names the commit; all also judges checks the branch does not require
    ValueTask<PrChecksResponse> ReadAsync(
        string repository,
        int? pullRequest,
        string? sha,
        string? branch,
        bool all,
        CancellationToken cancellationToken
    );
}
