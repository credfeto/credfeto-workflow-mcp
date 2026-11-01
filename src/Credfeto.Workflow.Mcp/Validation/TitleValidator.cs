using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;

namespace Credfeto.Workflow.Mcp.Validation;

public static partial class TitleValidator
{
    public const string INVALID_TITLE_MESSAGE =
        "invalid value for --title: it must not be blank or longer than 256 characters";

    public const string INVALID_ISSUE_TITLE_MESSAGE =
        "invalid value for --title: it must be \"prefix: text\" or \"prefix(name): text\" with a lower-case prefix, for example \"oneshot: ...\" or \"tool(cfwf): ...\"";

    public const string INVALID_PULL_REQUEST_TITLE_MESSAGE =
        "invalid value for --title: it must be a Conventional Commits title, <type>[(<scope>)][!]: <description>";

    public const int MAX_TITLE_LENGTH = 256;
    public const int MAX_LABEL_LENGTH = 50;

    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;
    private const string TEXT_GROUP = "text";
    private const int UTF32_BYTES_PER_RUNE = 4;

    private static readonly string[] IssueTitleOwners = ["credfeto", "funfair-tech"];

    // The common checks every title must pass; callers check this first to choose between the general message and
    // the shape-specific one
    public static bool IsValidTitle(string title)
    {
        return IsValidText(value: title, maxLength: MAX_TITLE_LENGTH);
    }

    public static bool IsValidLabel(string label)
    {
        return IsValidText(value: label, maxLength: MAX_LABEL_LENGTH)
            && !label.Contains(value: ',', comparisonType: StringComparison.Ordinal);
    }

    // The shape rule is a house style of the listed owners only; every other owner just gets the common checks
    public static bool IsValidIssueTitle(string owner, string title)
    {
        if (!IsValidTitle(title))
        {
            return false;
        }

        if (!IsIssueTitleOwner(owner))
        {
            return true;
        }

        return HasVisibleCapturedText(match: IssueTitle().Match(title));
    }

    public static bool IsValidPullRequestTitle(string title)
    {
        return IsValidTitle(title) && HasVisibleCapturedText(match: PullRequestTitle().Match(title));
    }

    public static bool IsValidText(string value, int maxLength)
    {
        return !GitRefFormat.ContainsControlCharacter(value)
            && HasVisibleText(value)
            && !HasMoreRunesThan(value: value, maximum: maxLength);
    }

    // Rune.IsWhiteSpace covers Unicode spaces such as U+3000, and the invisible set covers the blank-looking code
    // points it does not
    public static bool HasVisibleText(string value)
    {
        return value.EnumerateRunes().Any(rune => !Rune.IsWhiteSpace(rune) && !InvisibleCharacters.Contains(rune));
    }

    // Length is counted in Unicode scalar values (runes) so an emoji counts once, as it does on GitHub. Every rune is
    // at least one UTF-16 char, so a string no longer than the maximum cannot exceed it, and UTF-32 spends exactly
    // four bytes per rune
    private static bool HasMoreRunesThan(string value, int maximum)
    {
        return value.Length > maximum && Encoding.UTF32.GetByteCount(value) / UTF32_BYTES_PER_RUNE > maximum;
    }

    private static bool IsIssueTitleOwner(string owner)
    {
        return IssueTitleOwners.Contains(value: owner, comparer: StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasVisibleCapturedText(Match match)
    {
        return match.Success && HasVisibleText(match.Groups[TEXT_GROUP].Value);
    }

    // The classes are ASCII-only to match cfwf's C-locale bracket expressions: [:space:] is exactly space, \t, \n,
    // \v, \f and \r, and \z is used because .NET's $ also matches before a trailing newline
    [GeneratedRegex(
        pattern: @"^[a-z][a-z0-9]*(-[a-z0-9]+)*(\([^(): \t\n\v\f\r]([^():]*[^(): \t\n\v\f\r])?\))?: +(?<text>.*)\z",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    public static partial Regex IssueTitle();

    [GeneratedRegex(
        pattern: @"^[a-z]+(\([^()]+\))?!?: (?<text>[^ \t\n\v\f\r].*)\z",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    public static partial Regex PullRequestTitle();
}
