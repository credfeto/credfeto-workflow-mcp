using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IPrReplyService
{
    ValueTask<PrReplyResult> ReplyAsync(
        string repository,
        int pullRequest,
        string commentId,
        string body,
        CancellationToken cancellationToken
    );
}
