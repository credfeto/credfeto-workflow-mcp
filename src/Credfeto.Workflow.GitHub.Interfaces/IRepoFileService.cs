using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IRepoFileService
{
    ValueTask<RepoFileResponse> ReadAsync(
        string repository,
        string path,
        string? gitRef,
        CancellationToken cancellationToken
    );
}
