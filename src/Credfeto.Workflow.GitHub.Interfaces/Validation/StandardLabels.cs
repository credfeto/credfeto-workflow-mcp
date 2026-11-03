using System;
using System.Collections.Frozen;

namespace Credfeto.Workflow.GitHub.Interfaces.Validation;

public static class StandardLabels
{
    public const string BLOCKED = "Blocked";
    public const string ON_HOLD = "On Hold";

    // The labels defined in .github/labels.yml, less the credfeto-workflow-* labels the workflow itself manages
    private static readonly FrozenSet<string> Standard = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        ".NET update",
        "AI-Work",
        "auto-pr",
        BLOCKED,
        "Bug",
        "C#",
        "C# Project",
        "C# Solution",
        "Change Log",
        "Changelog Not Required",
        "Config Change",
        "dependencies",
        "DO NOT MERGE",
        "dotnet",
        "Editorconfig",
        "Enhancement",
        "github-actions",
        "High",
        "Low",
        "Markdown",
        "Medium",
        "Migration Script",
        "never-close",
        "no-pr-activity",
        "npm",
        ON_HOLD,
        "Performance",
        "Powershell",
        "Read Me",
        "Refactor",
        "Security",
        "Setup",
        "Solidity",
        "SQL",
        "Static Code Analysis Rules",
        "Tech Debt",
        "Unit Tests",
        "Urgent"
    );

    // Priority is set by a human, so an agent may neither add nor remove one
    private static readonly FrozenSet<string> Priority = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "Security",
        "Urgent",
        "High",
        "Medium",
        "Low"
    );

    public static string? FindStandard(string value)
    {
        return Standard.TryGetValue(equalValue: value, actualValue: out string? standard) ? standard : null;
    }

    public static bool IsPriority(string value)
    {
        return Priority.Contains(value);
    }
}
