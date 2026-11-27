using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface ICommitCheckRunsService
{
    // Lists the check runs on one commit, every page, without judging them against any branch rules
    ValueTask<CommitCheckRunsResponse> ReadAsync(string repository, string sha, CancellationToken cancellationToken);
}
