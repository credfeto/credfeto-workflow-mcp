using System;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Credfeto.Workflow.GitHub.Http;

internal static class UriHosts
{
    // Enough for any legitimately encoded value; a segment still changing after this many passes is treated as
    // traversing, so an attacker cannot out-nest the check
    private const int MAX_DECODE_PASSES = 4;

    private static readonly FrozenSet<string> DotSegments = new[] { ".", ".." }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly char[] PathSeparators = ['/', '\\'];

    private static readonly char[] SegmentSuffixDelimiters = [';', '?', '#'];

    public static bool IsSameServer(Uri link, Uri baseAddress)
    {
        return Uri.Compare(
                uri1: link,
                uri2: baseAddress,
                partsToCompare: UriComponents.SchemeAndServer,
                compareFormat: UriFormat.Unescaped,
                comparisonType: StringComparison.OrdinalIgnoreCase
            ) == 0;
    }

    // Network-path references such as //other.host/path resolve to a different host, and rooted (/path) or
    // dot-segment (../path) references resolve outside the base path; resolving first and checking the result
    // catches all of them. The options validator guarantees the base path ends in '/', so the prefix match is
    // segment-aligned
    public static bool TryResolveUnderBasePath(Uri baseAddress, Uri relative, [NotNullWhen(true)] out Uri? resolved)
    {
        return Uri.TryCreate(baseUri: baseAddress, relativeUri: relative, out resolved)
            && IsUnderBasePath(link: resolved, baseAddress: baseAddress);
    }

    public static bool IsUnderBasePath(Uri link, Uri baseAddress)
    {
        return IsSameServer(link: link, baseAddress: baseAddress)
            && link.AbsolutePath.StartsWith(value: baseAddress.AbsolutePath, comparisonType: StringComparison.Ordinal)
            && !HasEncodedPathTraversal(link);
    }

    // System.Uri collapses literal and singly encoded (%2e) dot segments but leaves encoded separators (%2f, %5c)
    // and double-encoded values (%252e, %252f) in place, so the prefix match above passes repos%2f..%2fadmin or
    // %252e%252e, while a proxy that decodes and normalises the path, possibly over several hops, resolves it
    // outside the base path with the caller's token. Each segment is decoded until stable and rejected only when it
    // never stabilises or a piece between separators could still resolve as a dot segment (see IsTraversingPiece),
    // so an encoded slash inside one parameter (labels/area%2Fapi) is still allowed
    public static bool HasEncodedPathTraversal(Uri uri)
    {
        return uri.Segments.Any(IsTraversingSegment);
    }

    private static bool IsTraversingSegment(string segment)
    {
        return !TryDecodeUntilStable(value: segment.TrimEnd('/'), out string? decoded)
            || decoded.Split(PathSeparators).Any(IsTraversingPiece);
    }

    // Rather than one deny-list entry per proxy normalisation, each decoded piece is reduced to what any proxy
    // could still resolve as a dot segment: servlet-style proxies drop a ;parameter (..;/admin), a proxy that
    // re-parses the decoded path ends it at '?' or '#' (..%3F/admin), and NUL truncation or whitespace trimming
    // (IIS and Windows path normalisation) turns ..%00 or ..%20 into '..'. An escape still present once decoding is
    // stable is invalid UTF-8 such as the overlong %c0%ae, which a lenient decoder resolves to '.', so it is
    // rejected outright
    private static bool IsTraversingPiece(string piece)
    {
        string beforeSuffix = piece.Split(separator: SegmentSuffixDelimiters, count: 2)[0];

        return DotSegments.Contains(TrimIgnorable(beforeSuffix).ToString()) || HasUndecodableEscape(piece);
    }

    private static ReadOnlySpan<char> TrimIgnorable(ReadOnlySpan<char> value)
    {
        int start = 0;

        while (start < value.Length && IsIgnorable(value[start]))
        {
            ++start;
        }

        int end = value.Length;

        while (end > start && IsIgnorable(value[end - 1]))
        {
            --end;
        }

        return value[start..end];
    }

    private static bool IsIgnorable(char value)
    {
        return char.IsWhiteSpace(value) || char.IsControl(value);
    }

    private static bool HasUndecodableEscape(string piece)
    {
        for (int index = 0; index < piece.Length; ++index)
        {
            if (Uri.IsHexEncoding(pattern: piece, index: index))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryDecodeUntilStable(string value, [NotNullWhen(true)] out string? decoded)
    {
        string current = value;

        for (int pass = 0; pass < MAX_DECODE_PASSES; ++pass)
        {
            string next = Uri.UnescapeDataString(current);

            if (StringComparer.Ordinal.Equals(x: next, y: current))
            {
                decoded = current;

                return true;
            }

            current = next;
        }

        decoded = null;

        return false;
    }
}
