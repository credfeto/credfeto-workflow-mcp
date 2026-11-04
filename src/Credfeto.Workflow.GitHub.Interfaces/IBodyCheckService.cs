using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IBodyCheckService
{
    // Runs the leak check every write of a body runs, without writing anything; a refusal is a result, not an error
    ValueTask<BodyCheckResult> CheckAsync(string repository, string body, CancellationToken cancellationToken);
}
