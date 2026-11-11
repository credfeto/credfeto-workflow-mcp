using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IRunRerunService
{
    ValueTask<RunRerunResult> RerunAsync(string repository, string run, CancellationToken cancellationToken);
}
