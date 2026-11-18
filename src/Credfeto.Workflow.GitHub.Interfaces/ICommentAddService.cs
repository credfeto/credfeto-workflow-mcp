using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface ICommentAddService
{
    ValueTask<CommentAddResult> AddAsync(
        string repository,
        int? pullRequest,
        int? issue,
        string body,
        CancellationToken cancellationToken
    );

    // For callers limited to issues: the number is checked first and refused when it names a pull request
    ValueTask<CommentAddResult> AddToIssueAsync(
        string repository,
        int? issue,
        string body,
        CancellationToken cancellationToken
    );
}
