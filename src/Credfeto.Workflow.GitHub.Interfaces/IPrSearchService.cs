using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IPrSearchService
{
    ValueTask<PrSearchResult> SearchAsync(
        string repository,
        string text,
        PrSearchState state,
        int limit,
        CancellationToken cancellationToken
    );
}
