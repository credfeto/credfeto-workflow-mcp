using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IIssueSearchService
{
    ValueTask<IssueSearchResult> SearchAsync(
        string repository,
        string text,
        IssueSearchState state,
        int limit,
        CancellationToken cancellationToken
    );
}
