using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IIssueCreateService
{
    // Every argument is validated and leak-checked, and the board resolved, before anything is written, so a call that
    // fails before the issue is created leaves no issue behind
    ValueTask<IssueCreateResponse> CreateAsync(
        string repository,
        string priority,
        string title,
        string body,
        IReadOnlyList<string>? labels,
        CancellationToken cancellationToken
    );
}
