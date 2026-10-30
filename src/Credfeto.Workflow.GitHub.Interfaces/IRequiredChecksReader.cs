using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IRequiredChecksReader
{
    // The head commit and the checks required by the branch's classic protection
    ValueTask<BranchInfo> ReadBranchAsync(string repository, string branch, CancellationToken cancellationToken);

    // The checks required by classic protection and by every ruleset that applies to the branch, without duplicates
    ValueTask<IReadOnlyList<RequiredCheck>> ReadRequiredChecksAsync(
        string repository,
        string branch,
        CancellationToken cancellationToken
    );

    // The distinct names of the required checks, in ordinal order
    ValueTask<BranchRequiredChecksResponse> ReadRequiredCheckNamesAsync(
        string repository,
        string branch,
        CancellationToken cancellationToken
    );
}
