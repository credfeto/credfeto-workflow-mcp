using System;
using System.Collections.Frozen;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.PullRequests;
using Credfeto.Workflow.GitHub.Runs;

namespace Credfeto.Workflow.GitHub.Workflows;

internal static class WorkflowListProjector
{
    // Only the states GitHub documents are passed on, so an unexpected value is never shown as if it were one
    private static readonly FrozenSet<string> States = FrozenSet.Create(
        equalityComparer: StringComparer.Ordinal,
        "active",
        "deleted",
        "disabled_fork",
        "disabled_inactivity",
        "disabled_manually"
    );

    public static WorkflowEntry Project(WorkflowResponse? workflow)
    {
        if (workflow is null)
        {
            throw new GitHubInvalidResponseException("GitHub returned an empty workflow");
        }

        string path = PullRequestViewText.Clean(workflow.Path);

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new GitHubInvalidResponseException("GitHub returned a workflow without a path");
        }

        return new(Name: RunText.Field(workflow.Name), Path: path, State: State(workflow.State));
    }

    private static string State(string? state)
    {
        return state is not null && States.Contains(state)
            ? state
            : throw new GitHubInvalidResponseException("GitHub returned a workflow with an unknown state");
    }
}
