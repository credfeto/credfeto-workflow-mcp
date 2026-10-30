using System;
using System.Buffers;
using System.Linq;

namespace Credfeto.Workflow.GitHub.Http;

// Upstream text shown to callers on one line cannot be allowed to start a new one, so anything that could is
// flattened to a space
internal static class UpstreamText
{
    private const char LINE_SEPARATOR = (char)0x2028;

    private const char PARAGRAPH_SEPARATOR = (char)0x2029;

    private static readonly SearchValues<char> LineBreakingCharacters = SearchValues.Create([
        .. Enumerable.Range(start: 0x00, count: 0x20).Select(static c => (char)c),
        .. Enumerable.Range(start: 0x7F, count: 0x21).Select(static c => (char)c),
        LINE_SEPARATOR,
        PARAGRAPH_SEPARATOR,
    ]);

    public static string ReplaceLineBreaking(string value)
    {
        if (!value.AsSpan().ContainsAny(LineBreakingCharacters))
        {
            return value;
        }

        return string.Create(
            length: value.Length,
            state: value,
            action: static (span, source) =>
            {
                for (int index = 0; index < source.Length; ++index)
                {
                    char character = source[index];
                    span[index] = IsLineBreaking(character) ? ' ' : character;
                }
            }
        );
    }

    // The line and paragraph separators end a line without being control characters
    private static bool IsLineBreaking(char character)
    {
        return char.IsControl(character) || character is LINE_SEPARATOR or PARAGRAPH_SEPARATOR;
    }
}
