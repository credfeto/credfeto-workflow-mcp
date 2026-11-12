using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IRunLogService
{
    // job limits the log to one job of the run; lines is how many of the last lines of each failed step to keep.
    // A grep pattern searches each failed step's whole log from line fromLine instead, ignoring lines, and keeps at
    // most maxMatches matching lines in all, each with context lines before and after it
    ValueTask<RunLogResponse> ReadFailedAsync(
        string repository,
        string run,
        string? job,
        int lines,
        string? grep,
        int context,
        int maxMatches,
        int fromLine,
        CancellationToken cancellationToken
    );
}
