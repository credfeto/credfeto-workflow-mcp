using System;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IPrChecksWaitService
{
    // The target is given as for IPrChecksService.ReadAsync; every read is reported to progress as it is made
    ValueTask<PrChecksWaitResponse> WaitAsync(
        string repository,
        int? pullRequest,
        string? sha,
        string? branch,
        bool all,
        int timeoutMinutes,
        IProgress<PrChecksResponse> progress,
        CancellationToken cancellationToken
    );
}
