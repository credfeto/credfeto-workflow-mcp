using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface ITagViewService
{
    // The commit the tag points to now, itself or through one annotated tag
    ValueTask<TagViewResponse> ViewAsync(string repository, string tag, CancellationToken cancellationToken);
}
