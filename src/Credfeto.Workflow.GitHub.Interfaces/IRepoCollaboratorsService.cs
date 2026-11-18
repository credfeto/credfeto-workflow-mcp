using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IRepoCollaboratorsService
{
    ValueTask<RepoCollaboratorsResponse> ListAsync(string repository, CancellationToken cancellationToken);
}
