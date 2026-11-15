using System;
using System.Buffers;
using System.Linq;

namespace Credfeto.Workflow.GitHub.Http;

// Upstream text shown to callers on one line cannot be allowed to start a new one, so anything that could is
// flattened to a space
internal static class UpstreamText
{
    private const char LINE_FEED = '\n';

    private const char TAB = '\t';

    // The line and paragraph separators end a line without being control characters
    private const char LINE_SEPARATOR = (char)0x2028;

    private const char PARAGRAPH_SEPARATOR = (char)0x2029;

    private static readonly SearchValues<char> LineBreakingCharacters = SearchValues.Create([
        .. Enumerable.Range(start: 0x00, count: 0x20).Select(static c => (char)c),
        .. Enumerable.Range(start: 0x7F, count: 0x21).Select(static c => (char)c),
        LINE_SEPARATOR,
        PARAGRAPH_SEPARATOR,
    ]);

    private static readonly SearchValues<char> LineBreakingCharactersExceptLayout = SearchValues.Create([
        .. Enumerable
            .Range(start: 0x00, count: 0x20)
            .Select(static c => (char)c)
            .Where(static c => c is not (LINE_FEED or TAB)),
        .. Enumerable.Range(start: 0x7F, count: 0x21).Select(static c => (char)c),
        LINE_SEPARATOR,
        PARAGRAPH_SEPARATOR,
    ]);

    public static string ReplaceLineBreaking(string value)
    {
        return Replace(value: value, unsafeCharacters: LineBreakingCharacters);
    }

    // Text a caller may print to a terminal over several lines, such as a diff, keeps the line feed and tab that lay
    // it out; with every other control character flattened, no escape character is left for a terminal escape
    // sequence to start with, and a carriage return or line separator cannot hide a line either
    public static string ReplaceLineBreakingKeepingLayout(string value)
    {
        return Replace(value: value, unsafeCharacters: LineBreakingCharactersExceptLayout);
    }

    private static string Replace(string value, SearchValues<char> unsafeCharacters)
    {
        if (!value.AsSpan().ContainsAny(unsafeCharacters))
        {
            return value;
        }

        return string.Create(
            length: value.Length,
            state: (Source: value, Unsafe: unsafeCharacters),
            action: static (span, state) =>
            {
                for (int index = 0; index < state.Source.Length; ++index)
                {
                    char character = state.Source[index];
                    span[index] = state.Unsafe.Contains(character) ? ' ' : character;
                }
            }
        );
    }
}
