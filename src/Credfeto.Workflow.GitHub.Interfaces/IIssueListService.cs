using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IIssueListService
{
    ValueTask<IssueListResponse> ListOpenIssuesAsync(string repository, CancellationToken cancellationToken);
}
