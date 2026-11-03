using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IIssueService
{
    ValueTask<IssueViewResponse> ViewAsync(string repository, int issue, CancellationToken cancellationToken);

    ValueTask<IssueComments> CommentsAsync(
        string repository,
        int issue,
        string? since,
        string? author,
        CancellationToken cancellationToken
    );

    ValueTask<IssueCloseResponse> CloseAsync(
        string repository,
        int issue,
        string reason,
        string? comment,
        CancellationToken cancellationToken
    );
}
