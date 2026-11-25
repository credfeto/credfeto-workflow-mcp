using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IIssuePostPlanService
{
    // Everything is checked before anything is written, then the plan is posted as a new comment, Blocked is added and
    // the issue is moved to Planning, always in that order, so Blocked and Planning are never set without a plan
    ValueTask<IssuePostPlanResponse> PostAsync(
        string repository,
        int issue,
        string body,
        CancellationToken cancellationToken
    );
}
