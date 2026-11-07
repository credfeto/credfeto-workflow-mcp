using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IRunViewService
{
    ValueTask<RunViewResponse> ViewAsync(string repository, string run, CancellationToken cancellationToken);
}
