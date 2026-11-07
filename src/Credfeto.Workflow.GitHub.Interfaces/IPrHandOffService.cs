using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IPrHandOffService
{
    // The record is optional; when given it follows the review record line that names the head
    ValueTask<PrHandOffResult> HandOffAsync(
        string repository,
        int pullRequest,
        string title,
        string description,
        string? record,
        CancellationToken cancellationToken
    );
}
