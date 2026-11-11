using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IRepositoryListService
{
    ValueTask<RepositoryListResponse> ListAsync(
        string owner,
        bool includeArchived,
        CancellationToken cancellationToken
    );
}
