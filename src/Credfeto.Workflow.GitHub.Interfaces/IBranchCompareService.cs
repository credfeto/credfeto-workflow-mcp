using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IBranchCompareService
{
    // How many commits the head branch is ahead of and behind the base branch, counted from their merge base
    ValueTask<BranchCompareResponse> CompareAsync(
        string repository,
        string baseBranch,
        string headBranch,
        CancellationToken cancellationToken
    );
}
