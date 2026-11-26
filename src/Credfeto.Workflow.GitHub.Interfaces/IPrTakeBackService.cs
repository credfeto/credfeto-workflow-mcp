using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IPrTakeBackService
{
    // The body is posted as a comment saying why the pull request was taken back
    ValueTask<PrTakeBackResult> TakeBackAsync(
        string repository,
        int pullRequest,
        string body,
        CancellationToken cancellationToken
    );
}
