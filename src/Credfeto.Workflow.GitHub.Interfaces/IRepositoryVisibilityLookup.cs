using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IRepositoryVisibilityLookup
{
    // A repository that does not exist and one the caller cannot see are both reported as NOT_FOUND
    ValueTask<RepositoryVisibility> GetVisibilityAsync(string repository, CancellationToken cancellationToken);
}
