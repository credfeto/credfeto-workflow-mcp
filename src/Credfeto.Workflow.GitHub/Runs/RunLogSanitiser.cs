using System;
using System.Text;
using System.Text.RegularExpressions;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Leaks;

namespace Credfeto.Workflow.GitHub.Runs;

// Log text is untrusted, so each line is cut, made printable, redacted and masked, in that order, before it is shown
public static partial class RunLogSanitiser
{
    public const int MAX_LINE_BYTES = 1000;

    public const string CUT_SUFFIX = " [cut]";
    public const string REDACTED_LINE = "[cfwf: line redacted: token]";
    public const string REDACTED_NAME = "[cfwf: name redacted: token]";
    public const string MASK = "***";

    private const int MAX_UTF8_SEQUENCE_BYTES = 4;
    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    // CSI, OSC (ended by BEL or ST, or by the end of the line) and the two-byte escapes
    private const string ANSI_ESCAPE = @"\x1B\[[0-?]*[ -/]*[@-~]|\x1B\][^\x07\x1B]*(\x07|\x1B\\)?|\x1B[@-Z\\-_]";

    private const string HEX_RUN = "[0-9A-Fa-f]{41,}";

    // '/' is left out of the alphabet so a path such as the runner's /home/runner/work/... is never taken for one run
    private const string BASE64_CHARACTER = "[A-Za-z0-9+=]";

    private const string BASE64_RUN =
        "(?<!"
        + BASE64_CHARACTER
        + ")(?="
        + BASE64_CHARACTER
        + "*[A-Z])(?="
        + BASE64_CHARACTER
        + "*[a-z])(?="
        + BASE64_CHARACTER
        + "*[0-9])"
        + BASE64_CHARACTER
        + "{41,}(?!"
        + BASE64_CHARACTER
        + ")";

    // A line of more than MAX_LINE_BYTES bytes is cut: bytes holds at least the first MAX_LINE_BYTES + 1 of it, so
    // the cut can be moved back to the start of the character it falls in
    public static RunLogLine Line(ReadOnlySpan<byte> bytes, bool over)
    {
        string decoded = Decode(over ? bytes[..CutAt(bytes)] : bytes);

        return RedactOnCheckFailure(
            check: () => Clean(decoded: decoded, over: over),
            redacted: new(Text: REDACTED_LINE, Redacted: true, Masked: false)
        );
    }

    public static string Name(string? name)
    {
        string printable = RunText.Field(name);

        return RedactOnCheckFailure(
            check: () => LeakPatterns.HasToken(printable) ? REDACTED_NAME : printable,
            redacted: REDACTED_NAME
        );
    }

    // Text that could not be checked in time is never shown, since it may hold a token
    public static T RedactOnCheckFailure<T>(Func<T> check, T redacted)
    {
        try
        {
            return check();
        }
        catch (Exception exception) when (exception is RegexMatchTimeoutException or LeakCheckFailedException)
        {
            return redacted;
        }
    }

    private static RunLogLine Clean(string decoded, bool over)
    {
        string printable = UpstreamText.ReplaceLineBreaking(AnsiEscape().Replace(input: decoded, replacement: ""));

        if (LeakPatterns.HasToken(printable))
        {
            return new(Text: REDACTED_LINE, Redacted: true, Masked: false);
        }

        string masked = Base64Run()
            .Replace(input: HexRun().Replace(input: printable, replacement: MASK), replacement: MASK);

        return new(
            Text: over ? masked + CUT_SUFFIX : masked,
            Redacted: false,
            Masked: !StringComparer.Ordinal.Equals(x: masked, y: printable)
        );
    }

    private static int CutAt(ReadOnlySpan<byte> bytes)
    {
        int cut = Math.Min(val1: MAX_LINE_BYTES, val2: bytes.Length);
        int earliest = Math.Max(val1: 0, val2: cut - (MAX_UTF8_SEQUENCE_BYTES - 1));

        while (cut > earliest && cut < bytes.Length && IsContinuation(bytes[cut]))
        {
            --cut;
        }

        return cut;
    }

    private static bool IsContinuation(byte value)
    {
        return (value & 0xC0) == 0x80;
    }

    // Text that is not valid UTF-8 keeps its ASCII and shows every other byte as '?'
    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (System.Text.Unicode.Utf8.IsValid(bytes))
        {
            return Encoding.UTF8.GetString(bytes);
        }

        return string.Create(
            length: bytes.Length,
            state: bytes.ToArray(),
            action: static (span, source) =>
            {
                for (int index = 0; index < source.Length; ++index)
                {
                    byte value = source[index];
                    span[index] = value < 0x80 ? (char)value : '?';
                }
            }
        );
    }

    [GeneratedRegex(
        pattern: ANSI_ESCAPE,
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex AnsiEscape();

    [GeneratedRegex(
        pattern: HEX_RUN,
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex HexRun();

    [GeneratedRegex(
        pattern: BASE64_RUN,
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex Base64Run();
}
