using System;
using System.Collections.Frozen;
using System.Linq;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Interfaces.Validation;

public static class StandardLabels
{
    public const string BLOCKED = "Blocked";
    public const string ON_HOLD = "On Hold";

    // The labels defined in .github/labels.yml, less the credfeto-workflow-* labels the workflow itself manages, with
    // the colour and description a label created by issue_create is given
    private static readonly StandardLabel[] Definitions =
    [
        new(Name: ".NET update", Colour: "a870c9", Description: "Update to .net global.json"),
        new(Name: "AI-Work", Colour: "ffa500", Description: "Work for an AI Agent"),
        new(Name: "auto-pr", Colour: "0000aa", Description: "Pull request created automatically"),
        new(Name: BLOCKED, Colour: "ff0000", Description: "Blocked by a dependency or external factor"),
        new(Name: "Bug", Colour: "d73a4a", Description: "Generic bug fix"),
        new(Name: "C#", Colour: "db6baa", Description: "C# Source Files"),
        new(Name: "C# Project", Colour: "db6baa", Description: "C# Project Files"),
        new(Name: "C# Solution", Colour: "db6baa", Description: "C# Solutions"),
        new(Name: "Change Log", Colour: "53fcd4", Description: "Changelog tracking file"),
        new(
            Name: "Changelog Not Required",
            Colour: "08f5f8",
            Description: "No changelog entry required for this pull request"
        ),
        new(Name: "Config Change", Colour: "d8bb50", Description: "Configuration files changes"),
        new(Name: "dependencies", Colour: "0366d6", Description: "Updates to dependencies"),
        new(Name: "DO NOT MERGE", Colour: "ff0000", Description: "This pull request should not be merged yet"),
        new(Name: "dotnet", Colour: "db6baa", Description: "Dotnet package updates"),
        new(Name: "Editorconfig", Colour: "00dead", Description: "Editor config file change"),
        new(Name: "Enhancement", Colour: "a2eeef", Description: "Enhancement to project"),
        new(Name: "github-actions", Colour: "e09cf4", Description: "Github actions workflow files"),
        new(Name: "High", Colour: "ffa500", Description: "High Priority"),
        new(Name: "Low", Colour: "cc8899", Description: "Low Priority"),
        new(Name: "Markdown", Colour: "5319e7", Description: "Markdown files"),
        new(Name: "Medium", Colour: "ffff00", Description: "Medium Priority"),
        new(Name: "Migration Script", Colour: "b680e5", Description: "SQL Migration scripts"),
        new(
            Name: "never-close",
            Colour: "1d76db",
            Description: "This issue should never be closed; it is a permanent tracking issue"
        ),
        new(Name: "no-pr-activity", Colour: "ffff00", Description: "Pull Request has had no activity for a long time"),
        new(Name: "npm", Colour: "e99695", Description: "npm package update"),
        new(Name: ON_HOLD, Colour: "ff0000", Description: "Do not work on this"),
        new(Name: "Performance", Colour: "0075ca", Description: "Performance enhancement or issue"),
        new(Name: "Powershell", Colour: "23bc12", Description: "Powershell Source Files"),
        new(Name: "Read Me", Colour: "5319e7", Description: "Repository readme file"),
        new(Name: "Refactor", Colour: "30027a", Description: "Code refactoring"),
        new(
            Name: "Security",
            Colour: "ee0701",
            Description: "Security issue, e.g. use of insecure packages, or security fix"
        ),
        new(Name: "Setup", Colour: "5319e7", Description: "Setup instructions"),
        new(Name: "Solidity", Colour: "413cd1", Description: "Solidity Source Files"),
        new(Name: "SQL", Colour: "413cd1", Description: "SQL Source Files"),
        new(
            Name: "Static Code Analysis Rules",
            Colour: "00dead",
            Description: "Ruleset for static code analysis files"
        ),
        new(Name: "Tech Debt", Colour: "30027a", Description: "Technical debt"),
        new(Name: "Unit Tests", Colour: "0e8a16", Description: "Unit test and integration test projects"),
        new(Name: "Urgent", Colour: "ff0000", Description: "Urgent Priority"),
    ];

    private static readonly FrozenDictionary<string, StandardLabel> Standard = Definitions.ToFrozenDictionary(
        keySelector: static label => label.Name,
        comparer: StringComparer.OrdinalIgnoreCase
    );

    // Priority is set by a human, so an agent may neither add nor remove one. Listed most urgent first
    private static readonly string[] PriorityNames = ["Security", "Urgent", "High", "Medium", "Low"];

    private static readonly FrozenSet<string> Priority = PriorityNames.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // The priorities as a sentence, for example in an error message: "Security, Urgent, High, Medium or Low"
    public static string PriorityList { get; } =
        string.Join(separator: ", ", value: PriorityNames, startIndex: 0, count: PriorityNames.Length - 1)
        + " or "
        + PriorityNames[^1];

    public static string? FindStandard(string value)
    {
        return FindDefinition(value)?.Name;
    }

    public static StandardLabel? FindDefinition(string value)
    {
        return Standard.TryGetValue(key: value, value: out StandardLabel? label) ? label : null;
    }

    public static bool IsPriority(string value)
    {
        return Priority.Contains(value);
    }

    // The priority in its canonical spelling, or null when the value is not one
    public static string? FindPriority(string value)
    {
        return Priority.TryGetValue(equalValue: value, actualValue: out string? priority) ? priority : null;
    }

    // The priority's rank, 0 being the most urgent; a value that is not a priority ranks after every priority
    public static int PriorityRank(string? value)
    {
        int rank = Array.FindIndex(
            array: PriorityNames,
            match: name => StringComparer.OrdinalIgnoreCase.Equals(x: name, y: value)
        );

        return rank < 0 ? PriorityNames.Length : rank;
    }
}
