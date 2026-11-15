using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IWorkflowRunService
{
    ValueTask<WorkflowRunResult> RunAsync(
        string repository,
        string workflow,
        string? gitRef,
        IReadOnlyList<string>? inputs,
        CancellationToken cancellationToken
    );
}
