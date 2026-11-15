using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IPrReviewCommentService
{
    ValueTask<PrReviewCommentResult> PostAsync(
        string repository,
        int pullRequest,
        string path,
        int line,
        int? startLine,
        string? side,
        string body,
        CancellationToken cancellationToken
    );
}
