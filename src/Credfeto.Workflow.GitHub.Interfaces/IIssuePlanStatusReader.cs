using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IIssuePlanStatusReader
{
    ValueTask<IssuePlanStatus> ReadAsync(string repository, int issue, CancellationToken cancellationToken);
}
