using System;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Leaks;

public sealed class LeakChecker : ILeakChecker
{
    private static readonly (LeakRule Rule, Func<string, bool> IsRefused)[] LineRules =
    [
        (LeakRule.TOKEN, LeakPatterns.HasToken),
        (LeakRule.HOST_PATH, LeakPatterns.HasHostPath),
        (LeakRule.CONTROL_CHARACTER, LeakPatterns.HasControlCharacter),
        (LeakRule.INVALID_UTF8, LeakPatterns.HasInvalidUtf8),
    ];

    public LeakRefusal? CheckBody(string body)
    {
        return CheckText(subject: LeakSubject.BODY, text: body)
            ?? (
                LeakPatterns.IsTooLong(body)
                    ? new(Subject: LeakSubject.BODY, Rule: LeakRule.TOO_LONG, LineNumber: null)
                    : null
            );
    }

    public LeakRefusal? CheckValue(LeakSubject subject, string value)
    {
        return CheckText(subject: subject, text: value);
    }

    // The text rules every write applies; a rule that needs more than the text (such as the repository being written
    // to) runs after this pass so the cheap checks refuse first.
    // Each rule scans every line before the next rule runs, so the reported rule is the first in rule order rather
    // than the first problem in the text, matching cfwf.
    public static LeakRefusal? CheckText(LeakSubject subject, string text)
    {
        string[] lines = text.Split('\n');

        foreach ((LeakRule rule, Func<string, bool> isRefused) in LineRules)
        {
            int? lineNumber = FindLine(isRefused: isRefused, lines: lines);

            if (lineNumber is not null)
            {
                return new(Subject: subject, Rule: rule, LineNumber: lineNumber);
            }
        }

        return null;
    }

    private static int? FindLine(Func<string, bool> isRefused, string[] lines)
    {
        int index = Array.FindIndex(array: lines, match: line => isRefused(line));

        return index < 0 ? null : index + 1;
    }
}
