using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Credfeto.Workflow.GitHub.PullRequests;

internal static partial class GraphQlCursor
{
    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    public static bool IsValid([NotNullWhen(true)] string? cursor)
    {
        return cursor is not null && Cursor().IsMatch(cursor);
    }

    // \z rather than $ because in .NET $ also matches before a trailing newline, which would let one through
    [GeneratedRegex(
        pattern: @"^[A-Za-z0-9+/=_-]+\z",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex Cursor();
}
