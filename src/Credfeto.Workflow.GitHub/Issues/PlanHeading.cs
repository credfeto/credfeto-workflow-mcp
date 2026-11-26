using System;
using System.Linq;

namespace Credfeto.Workflow.GitHub.Issues;

// Shared so the plan a post is checked for is the plan the plan-status read finds
internal static class PlanHeading
{
    public const string TEXT = "## Implementation Plan";

    public static bool IsPresent(string body)
    {
        return body.Split('\n').Any(line => StringComparer.Ordinal.Equals(x: line.TrimEnd('\r'), y: TEXT));
    }
}
