using System;
using System.Buffers;

namespace Credfeto.Workflow.GitHub.Interfaces.Validation;

public static class GitRefFormat
{
    private static readonly SearchValues<char> ForbiddenCharacters = SearchValues.Create(" ~^:?*[\\");

    public static bool IsControlCharacter(char value)
    {
        return value is < ' ' or >= '\u007F' and <= '\u009F';
    }

    public static bool ContainsControlCharacter(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan();

        return span.ContainsAnyInRange(lowInclusive: '\0', highInclusive: '\u001F')
            || span.ContainsAnyInRange(lowInclusive: '\u007F', highInclusive: '\u009F');
    }

    public static bool IsValidRefName(string name)
    {
        return name.Length != 0
            && !StringComparer.Ordinal.Equals(x: name, y: "@")
            && !HasForbiddenSequence(name)
            && !HasForbiddenCharacter(name)
            && name[0] != '/'
            && name[^1] is not '/' and not '.'
            && !HasInvalidComponent(name);
    }

    private static bool HasForbiddenSequence(string name)
    {
        return name.Contains(value: "..", comparisonType: StringComparison.Ordinal)
            || name.Contains(value: "@{", comparisonType: StringComparison.Ordinal)
            || name.Contains(value: "//", comparisonType: StringComparison.Ordinal);
    }

    private static bool HasForbiddenCharacter(string name)
    {
        return name.AsSpan().ContainsAny(ForbiddenCharacters) || ContainsControlCharacter(name);
    }

    // A component is the text between slashes; none may start with '.' or end with ".lock"
    private static bool HasInvalidComponent(string name)
    {
        return name[0] == '.'
            || name.Contains(value: "/.", comparisonType: StringComparison.Ordinal)
            || name.EndsWith(value: ".lock", comparisonType: StringComparison.Ordinal)
            || name.Contains(value: ".lock/", comparisonType: StringComparison.Ordinal);
    }
}
