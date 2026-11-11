using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface ILabelService
{
    ValueTask<LabelChangeResponse> AddAsync(
        string repository,
        int? pullRequest,
        int? issue,
        IReadOnlyList<string> labels,
        CancellationToken cancellationToken
    );

    ValueTask<LabelChangeResponse> RemoveAsync(
        string repository,
        int? pullRequest,
        int? issue,
        IReadOnlyList<string> labels,
        CancellationToken cancellationToken
    );

    ValueTask<LabelCreateResponse> CreateAsync(
        string repository,
        string name,
        string description,
        string colour,
        CancellationToken cancellationToken
    );
}
