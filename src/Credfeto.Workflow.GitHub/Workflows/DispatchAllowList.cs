using System;
using System.Collections.Generic;
using System.Linq;
using Credfeto.Workflow.GitHub.Interfaces.Validation;

namespace Credfeto.Workflow.GitHub.Workflows;

// Each line is "<file.yml> [<ref>...]", where "*" permits any ref. A line that cannot be read is ignored rather than
// guessed at, so a mistake in the file refuses a workflow instead of permitting one
public static class DispatchAllowList
{
    private const char BYTE_ORDER_MARK = (char)0xFEFF;
    private const char COMMENT = '#';
    private const char TAB = '\t';
    private const string ANY_REF = "*";

    public static IReadOnlyDictionary<string, DispatchAllowEntry> Parse(string text)
    {
        Dictionary<string, HashSet<string>> refs = [with(StringComparer.Ordinal)];
        HashSet<string> anyRef = [with(StringComparer.Ordinal)];

        foreach (string line in text.TrimStart(BYTE_ORDER_MARK).Split('\n'))
        {
            if (ReadLine(line) is not { } tokens)
            {
                continue;
            }

            string workflow = tokens[0];

            if (!refs.TryGetValue(key: workflow, value: out HashSet<string>? permitted))
            {
                permitted = [with(StringComparer.Ordinal)];
                refs.Add(key: workflow, value: permitted);
            }

            foreach (string gitRef in tokens.Skip(1))
            {
                if (StringComparer.Ordinal.Equals(x: gitRef, y: ANY_REF))
                {
                    anyRef.Add(workflow);
                }
                else
                {
                    permitted.Add(gitRef);
                }
            }
        }

        return refs.ToDictionary(
            keySelector: pair => pair.Key,
            elementSelector: pair => new DispatchAllowEntry(AnyRef: anyRef.Contains(pair.Key), Refs: pair.Value),
            comparer: StringComparer.Ordinal
        );
    }

    // Null for a blank line, a comment, or a line that cannot be read
    private static IReadOnlyList<string>? ReadLine(string line)
    {
        if (line.Contains(TAB, StringComparison.Ordinal))
        {
            return null;
        }

        IReadOnlyList<string> tokens =
        [
            .. line.TrimEnd('\r')
                .Split(separator: ' ', options: StringSplitOptions.RemoveEmptyEntries)
                .TakeWhile(token => token[0] != COMMENT),
        ];

        if (tokens.Count == 0 || !WorkflowFileName.IsValid(tokens[0]))
        {
            return null;
        }

        return tokens.Skip(1).All(IsRef) ? tokens : null;
    }

    private static bool IsRef(string token)
    {
        if (StringComparer.Ordinal.Equals(x: token, y: ANY_REF))
        {
            return true;
        }

        return GitRefFormat.IsValidRefName(token)
            && token[0] != '-'
            && !StringComparer.Ordinal.Equals(x: token, y: "HEAD");
    }
}
