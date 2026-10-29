using System;
using System.Buffers;
using System.Linq;

namespace Credfeto.Workflow.GitHub.Interfaces.Validation;

public static class GitRefFormat
{
    private static readonly SearchValues<char> ForbiddenCharacters = SearchValues.Create(" ~^:?*[\\");

    public static bool ContainsControlCharacter(string value)
    {
        return value.Any(char.IsControl);
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
