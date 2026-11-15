using System;
using System.Buffers;
using System.Linq;

namespace Credfeto.Workflow.GitHub.PullRequests;

// A diff is upstream text that a caller may print to a terminal, so every control character but the line feed and
// tab that lay the diff out is replaced with a space; with no escape character left, no terminal escape sequence can
// survive, and a carriage return or line separator cannot hide a line either
internal static class PrDiffText
{
    private const char LINE_FEED = '\n';

    private const char TAB = '\t';

    private const char LINE_SEPARATOR = (char)0x2028;

    private const char PARAGRAPH_SEPARATOR = (char)0x2029;

    private static readonly SearchValues<char> Unsafe = SearchValues.Create([
        .. Enumerable
            .Range(start: 0x00, count: 0x20)
            .Select(static c => (char)c)
            .Where(static c => c is not (LINE_FEED or TAB)),
        .. Enumerable.Range(start: 0x7F, count: 0x21).Select(static c => (char)c),
        LINE_SEPARATOR,
        PARAGRAPH_SEPARATOR,
    ]);

    public static string Neutralise(string value)
    {
        if (!value.AsSpan().ContainsAny(Unsafe))
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
                    span[index] = Unsafe.Contains(character) ? ' ' : character;
                }
            }
        );
    }
}
