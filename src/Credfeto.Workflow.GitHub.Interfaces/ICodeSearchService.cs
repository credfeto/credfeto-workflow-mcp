using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface ICodeSearchService
{
    ValueTask<CodeSearchResult> SearchAsync(
        string repository,
        string text,
        int limit,
        CancellationToken cancellationToken
    );
}
