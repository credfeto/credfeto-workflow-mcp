using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IAdvisoryViewService
{
    ValueTask<AdvisoryViewResult> ViewAsync(string ghsa, CancellationToken cancellationToken);
}
