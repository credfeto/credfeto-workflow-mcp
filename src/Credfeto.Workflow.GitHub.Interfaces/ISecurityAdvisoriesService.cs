using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface ISecurityAdvisoriesService
{
    ValueTask<SecurityAdvisoriesResult> ListAsync(string repository, CancellationToken cancellationToken);
}
