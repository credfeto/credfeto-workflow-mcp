using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IRunLogService
{
    // job limits the log to one job of the run; lines is how many of the last lines of each failed step to keep
    ValueTask<RunLogResponse> ReadFailedAsync(
        string repository,
        string run,
        string? job,
        int lines,
        CancellationToken cancellationToken
    );
}
