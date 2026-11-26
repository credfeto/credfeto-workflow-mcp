using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IBlockService
{
    ValueTask<BlockResult> BlockIssueAsync(
        string repository,
        int issue,
        string body,
        CancellationToken cancellationToken
    );

    ValueTask<BlockResult> BlockPullRequestAsync(
        string repository,
        int pullRequest,
        string body,
        CancellationToken cancellationToken
    );
}
