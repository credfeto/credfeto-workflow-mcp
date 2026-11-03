using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IPullRequestListService
{
    ValueTask<PullRequestListResponse> ListAsync(
        string repository,
        int? issue,
        string? head,
        IReadOnlyList<string>? labels,
        CancellationToken cancellationToken
    );
}
