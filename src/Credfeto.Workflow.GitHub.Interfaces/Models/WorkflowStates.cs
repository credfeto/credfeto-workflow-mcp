using System;
using System.Collections.Generic;
using System.Linq;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;

namespace Credfeto.Workflow.GitHub.Interfaces.Models;

public static class WorkflowStates
{
    public const string NOT_STARTED = "Not Started";
    public const string PLANNING = "Planning";
    public const string APPROVED = "Approved";
    public const string DEVELOPMENT = "Development";
    public const string AI_SIMPLIFY = "AI Simplify";
    public const string AI_REVIEW = "AI Review";
    public const string AI_SECURITY_REVIEW = "AI Security Review";
    public const string AI_COVERAGE = "AI Coverage";
    public const string HUMAN_REVIEW = "Human Review";
    public const string COMPLETE = "Complete";

    // Board order, which is also the order the valid states are listed in when a state is not recognised
    public static IReadOnlyList<string> All { get; } =
    [
        NOT_STARTED,
        PLANNING,
        APPROVED,
        DEVELOPMENT,
        AI_SIMPLIFY,
        AI_REVIEW,
        AI_SECURITY_REVIEW,
        AI_COVERAGE,
        HUMAN_REVIEW,
        COMPLETE,
    ];

    public static string Canonicalise(string state)
    {
        string value = GitHubNames.Status(value: state, parameterName: "status");

        return All.FirstOrDefault(candidate => StringComparer.OrdinalIgnoreCase.Equals(x: candidate, y: value))
            ?? throw new UnknownWorkflowStateException(
                $"unknown --status (valid: {string.Join(separator: ", ", values: All)})"
            );
    }
}
