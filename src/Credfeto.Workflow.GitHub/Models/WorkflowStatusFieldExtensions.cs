using System;
using System.Linq;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Models;

public static class WorkflowStatusFieldExtensions
{
    public static string? FindOptionId(this WorkflowStatusField statusField, string name)
    {
        if (statusField.OptionIds.TryGetValue(key: name, value: out string? exact))
        {
            return exact;
        }

        // Project option names can differ only by case, so a case-insensitive match is only safe when it is unique
        string[] matches =
        [
            .. statusField
                .OptionIds.Where(option => StringComparer.OrdinalIgnoreCase.Equals(x: option.Key, y: name))
                .Select(option => option.Value)
                .Take(2),
        ];

        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            // The field and option names are upstream content, so the fixed default message is used
            _ => throw new AmbiguousStatusOptionException(),
        };
    }
}
