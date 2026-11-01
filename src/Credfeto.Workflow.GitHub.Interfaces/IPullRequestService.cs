using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IPullRequestService
{
    ValueTask<PullRequestView> ViewAsync(
        string repository,
        int pullRequest,
        IReadOnlyList<string>? fields,
        CancellationToken cancellationToken
    );
}
