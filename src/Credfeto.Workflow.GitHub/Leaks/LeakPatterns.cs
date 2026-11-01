using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;

namespace Credfeto.Workflow.GitHub.Leaks;

// The patterns are transcribed from cfwf's POSIX ERE leak patterns. .NET has no [[:space:]] bracket class, so the
// C locale's space characters are spelled out instead of \s, which would also match Unicode spaces.
public static partial class LeakPatterns
{
    public const int MAXIMUM_BODY_LENGTH = 65536;

    internal const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    private const string SPACE = @"[ \t\n\v\f\r]";
    private const string NOT_SPACE = @"[^ \t\n\v\f\r]";
    private const string URL_USER = @"://[^ \t\n\v\f\r/:?#]*";
    private const string URL_SECRET = @"[^ \t\n\v\f\r/?#@""`]+";
    private const string CREDENTIAL_PASSWORD = ":@*" + URL_SECRET + "@";

    private const string PREFIXED_TOKEN =
        "(^|[^A-Za-z0-9])((gh[porsu]_|xox[abprs]-|glpat-|sk-ant-|sk-proj-)[A-Za-z0-9_-]"
        + "|npm_[A-Za-z0-9]{36}|AIza[A-Za-z0-9_-]{35}|sk-[A-Za-z0-9]{48}|x-access-token"
        + CREDENTIAL_PASSWORD
        + ")";

    private const string UNANCHORED_TOKEN =
        "github_pat_[A-Za-z0-9_]|A[KS]IA[0-9A-Z]{16}|-----BEGIN ([A-Z0-9]+ )*PRIVATE KEY-----"
        + @"|[Dd]iscord(app)?\.com/api/webhooks/[0-9]+/[A-Za-z0-9_-]";

    private const string URL_CREDENTIAL = URL_USER + CREDENTIAL_PASSWORD;

    private const string AUTHORIZATION_HEADER =
        @"([Aa]uthorization|AUTHORIZATION)(\\?[""'])?" + SPACE + "*[:=]" + SPACE + "*" + NOT_SPACE;

    // A bearer credential is a run of at least 20 token characters holding a digit, so prose such as
    // "a Bearer token" or a long word after Bearer is not mistaken for one
    private const string BEARER_TOKEN =
        "(^|[^A-Za-z0-9])(Bearer|bearer|BEARER)" + SPACE + "+(?=[A-Za-z._~+/=-]*[0-9])[A-Za-z0-9._~+/=-]{20}";

    // An empty value or a $-reference to a variable is not a secret
    private const string SECRET_ASSIGNMENT =
        @"(^|[^A-Za-z0-9_])([A-Z0-9_]*_)?(TOKEN|SECRET|KEY|PASSWORD)=[""']?[^ \t\n\v\f\r$""']";

    private const string TOKEN =
        PREFIXED_TOKEN
        + "|"
        + UNANCHORED_TOKEN
        + "|"
        + URL_CREDENTIAL
        + "|"
        + AUTHORIZATION_HEADER
        + "|"
        + BEARER_TOKEN
        + "|"
        + SECRET_ASSIGNMENT;

    private const string HOST_DIRECTORY = "/(home|root|run/user|tmp|Users)/";

    // A path relative to . or ~, or a URL path, is not a path on the host, so the directory must start the path
    private const string HOST_PATH =
        "(^|^[^A-Za-z0-9.~/-]|[^/][^A-Za-z0-9.~/-]|/)"
        + HOST_DIRECTORY
        + "|(^|[^A-Za-z0-9.~/-]|file://)(/[A-Za-z0-9._-]+)+"
        + HOST_DIRECTORY
        + @"|(^|[^A-Za-z0-9])[A-Za-z]:[\\/]+[Uu][Ss][Ee][Rr][Ss][\\/]";

    // Tab is allowed and a line never holds a newline; a carriage return is refused so CRLF text is caught
    private const string CONTROL_CHARACTER = @"[\x00-\x08\x0B-\x1F\x7F-\x9F]";

    public static bool HasToken(string line)
    {
        return IsMatch(pattern: Token(), line: line);
    }

    public static bool HasHostPath(string line)
    {
        return IsMatch(pattern: HostPath(), line: line);
    }

    public static bool HasControlCharacter(string line)
    {
        return IsMatch(pattern: ControlCharacter(), line: line);
    }

    // A string holds UTF-16, so text that was not valid UTF-8 shows up as a lone surrogate or the replacement
    // character a decoder substituted for the invalid bytes
    public static bool HasInvalidUtf8(string line)
    {
        ReadOnlySpan<char> remaining = line;

        while (!remaining.IsEmpty)
        {
            OperationStatus status = Rune.DecodeFromUtf16(source: remaining, out Rune rune, out int consumed);

            if (status != OperationStatus.Done || rune == Rune.ReplacementChar)
            {
                return true;
            }

            remaining = remaining[consumed..];
        }

        return false;
    }

    // GitHub limits a body in characters, which a surrogate pair counts as one of
    public static bool IsTooLong(string body)
    {
        if (body.Length <= MAXIMUM_BODY_LENGTH)
        {
            return false;
        }

        return body.EnumerateRunes().Skip(MAXIMUM_BODY_LENGTH).Any();
    }

    // The timeout exception is not wrapped: it carries the text being checked, which must never be surfaced
    public static bool IsMatch(Regex pattern, string line)
    {
        return TryMatch(pattern: pattern, line: line)
            ?? throw new LeakCheckFailedException("the text could not be checked for leaks in time");
    }

    public static IReadOnlyList<Match> Matches(Regex pattern, string line)
    {
        return TryMatches(pattern: pattern, line: line)
            ?? throw new LeakCheckFailedException("the text could not be checked for leaks in time");
    }

    private static IReadOnlyList<Match>? TryMatches(Regex pattern, string line)
    {
        try
        {
            MatchCollection matches = pattern.Matches(line);

            // MatchCollection is lazy and reading Count runs every match, so a timeout is raised here where it is caught
            _ = matches.Count;

            return matches;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static bool? TryMatch(Regex pattern, string line)
    {
        try
        {
            return pattern.IsMatch(line);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    [GeneratedRegex(
        pattern: TOKEN,
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex Token();

    [GeneratedRegex(
        pattern: HOST_PATH,
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex HostPath();

    [GeneratedRegex(
        pattern: CONTROL_CHARACTER,
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex ControlCharacter();
}
