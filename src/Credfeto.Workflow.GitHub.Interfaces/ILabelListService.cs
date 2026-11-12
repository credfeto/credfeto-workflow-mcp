using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface ILabelListService
{
    ValueTask<LabelListResponse> ListAsync(string repository, CancellationToken cancellationToken);
}
