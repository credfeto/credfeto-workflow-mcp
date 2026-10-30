using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IPullRequestCommentsReader
{
    ValueTask<PullRequestComments> ReadAsync(string repository, int pullRequest, CancellationToken cancellationToken);
}
