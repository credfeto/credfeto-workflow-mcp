using System.Diagnostics;
using System.Runtime.InteropServices;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;

namespace Credfeto.Workflow.GitHub.Board;

[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{Kind} {Number}")]
internal readonly record struct BoardWorkItem(WorkItemKind Kind, int Number)
{
    // The work item is validated before the board is resolved, so an invalid one is reported as such rather than as
    // a board failure
    public static BoardWorkItem Validate(int? pullRequest, int? issue)
    {
        if (pullRequest.HasValue == issue.HasValue)
        {
            throw ParameterValidationException.For(
                parameterName: "pr",
                rule: "exactly one of pr or issue must be given"
            );
        }

        if (pullRequest is { } pullRequestNumber)
        {
            return pullRequestNumber > 0
                ? new(Kind: WorkItemKind.PULL_REQUEST, Number: pullRequestNumber)
                : throw ParameterValidationException.For(parameterName: nameof(pullRequest), rule: "must be above 0");
        }

        int issueNumber = issue.GetValueOrDefault();

        return issueNumber > 0
            ? new(Kind: WorkItemKind.ISSUE, Number: issueNumber)
            : throw ParameterValidationException.For(parameterName: nameof(issue), rule: "must be above 0");
    }
}
