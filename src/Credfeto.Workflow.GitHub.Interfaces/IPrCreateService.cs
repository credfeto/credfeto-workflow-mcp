using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IPrCreateService
{
    // Every argument is validated and leak-checked, and the board resolved, before anything is written, so a call that
    // fails before the pull request is created leaves no pull request behind
    ValueTask<PrCreateResponse> CreateAsync(
        string repository,
        string head,
        string? baseBranch,
        string title,
        string body,
        CancellationToken cancellationToken
    );
}
