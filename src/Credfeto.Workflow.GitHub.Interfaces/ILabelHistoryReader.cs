using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface ILabelHistoryReader
{
    ValueTask<LabelHistoryResult> ReadAsync(
        string repository,
        int? issue,
        int? pr,
        string? label,
        CancellationToken cancellationToken
    );
}
