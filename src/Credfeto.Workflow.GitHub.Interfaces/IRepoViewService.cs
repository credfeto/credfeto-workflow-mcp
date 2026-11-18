using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IRepoViewService
{
    ValueTask<RepoViewResult> ViewAsync(string repository, CancellationToken cancellationToken);
}
