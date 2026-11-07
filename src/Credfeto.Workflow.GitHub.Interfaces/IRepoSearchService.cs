using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IRepoSearchService
{
    ValueTask<RepoSearchResult> SearchAsync(
        string owner,
        string text,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken
    );
}
