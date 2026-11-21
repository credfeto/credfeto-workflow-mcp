using System;
using System.Buffers;
using System.Linq;
using System.Text;

namespace Credfeto.Workflow.GitHub.Interfaces.Validation;

public static class GitHubNames
{
    private const int MAX_COMMENT_ID_DIGITS = 19;
    private const int COMMIT_SHA_LENGTH = 40;
    private const int MAX_LABEL_LENGTH = 50;
    private const int LABEL_COLOUR_LENGTH = 6;
    private const int MAX_LABEL_DESCRIPTION_LENGTH = 100;
    private const int MAX_REPOSITORY_PATH_LENGTH = 4096;
    private const string BOT_SUFFIX = "[bot]";
    private const string GHSA_PREFIX = "GHSA";
    private const int GHSA_GROUPS = 3;
    private const int GHSA_GROUP_LENGTH = 5;
    private const int GHSA_ID_LENGTH = 19;

    private static readonly SearchValues<char> RepositoryNameCharacters = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_.-"
    );

    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    private static readonly SearchValues<char> LoginCharacters = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-"
    );

    private static readonly SearchValues<char> OwnerCharacters = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-"
    );

    private static readonly SearchValues<char> GhsaCharacters = SearchValues.Create("23456789cfghjmpqrvwx");

    public static string Repository(string value, string parameterName)
    {
        int separator = value.IndexOf('/', StringComparison.Ordinal);

        ThrowIf(
            invalid: separator < 0 || value.AsSpan(separator + 1).Contains('/'),
            parameterName: parameterName,
            rule: "it must be in the form owner/repo"
        );

        ThrowIf(
            invalid: !IsRepositoryPart(value.AsSpan(0, separator)) || !IsRepositoryPart(value.AsSpan(separator + 1)),
            parameterName: parameterName,
            rule: "owner and repo may only contain letters, digits, '_', '.' and '-' and must not be '.' or '..'"
        );

        return value;
    }

    public static string Number(string value, string parameterName)
    {
        ThrowIf(
            invalid: value.Length == 0
                || value.AsSpan().ContainsAnyExceptInRange(lowInclusive: '0', highInclusive: '9'),
            parameterName: parameterName,
            rule: "it must contain only the digits 0-9"
        );

        string trimmed = value.TrimStart('0');

        return trimmed.Length == 0 ? "0" : trimmed;
    }

    public static string PositiveNumber(string value, string parameterName)
    {
        string number = Number(value: value, parameterName: parameterName);

        ThrowIf(
            invalid: StringComparer.Ordinal.Equals(x: number, y: "0"),
            parameterName: parameterName,
            rule: "it must be greater than zero"
        );

        return number;
    }

    // The digit limit applies after leading zeros are removed, so it bounds the numeric value rather than the input text
    public static string CommentId(string value, string parameterName)
    {
        string number = PositiveNumber(value: value, parameterName: parameterName);

        ThrowIf(
            invalid: number.Length > MAX_COMMENT_ID_DIGITS,
            parameterName: parameterName,
            rule: "it must have at most 19 significant digits"
        );

        return number;
    }

    public static string CommitSha(string value, string parameterName)
    {
        ThrowIf(
            invalid: value.Length != COMMIT_SHA_LENGTH || value.AsSpan().ContainsAnyExcept(HexDigits),
            parameterName: parameterName,
            rule: "it must be a full 40 character hexadecimal commit SHA"
        );

        return HexToLower(value);
    }

    public static string Branch(string value, string parameterName)
    {
        Tag(value: value, parameterName: parameterName);

        ThrowIf(
            invalid: value[0] == '-' || StringComparer.Ordinal.Equals(x: value, y: "HEAD"),
            parameterName: parameterName,
            rule: "a branch name must not start with '-' or be 'HEAD'"
        );

        return value;
    }

    public static string Tag(string value, string parameterName)
    {
        ThrowIf(
            invalid: !GitRefFormat.IsValidRefName(value),
            parameterName: parameterName,
            rule: "it must be a valid git ref name"
        );

        return value;
    }

    // A full commit SHA is also a valid ref name, so this one rule covers a branch, a tag and a commit
    public static string GitRef(string value, string parameterName)
    {
        ThrowIf(
            invalid: !GitRefFormat.IsValidRefName(value),
            parameterName: parameterName,
            rule: "it must be a valid git branch or tag name, or a commit SHA"
        );

        return value;
    }

    public static string RepositoryPath(string value, string parameterName)
    {
        ThrowIf(
            invalid: value.Length > MAX_REPOSITORY_PATH_LENGTH,
            parameterName: parameterName,
            rule: "it must be at most 4096 characters long"
        );

        ThrowIf(
            invalid: GitRefFormat.ContainsControlCharacter(value),
            parameterName: parameterName,
            rule: "it must not contain control characters"
        );

        ThrowIf(
            invalid: value.Split('/').Any(IsInvalidPathPart),
            parameterName: parameterName,
            rule: "it must not start or end with '/' or have an empty, '.' or '..' part"
        );

        return value;
    }

    public static string Status(string value, string parameterName)
    {
        ThrowIf(
            invalid: GitRefFormat.ContainsControlCharacter(value),
            parameterName: parameterName,
            rule: "it must not contain control characters"
        );

        return value;
    }

    // Length is counted in Unicode scalar values (runes) so an emoji counts once, as it does on GitHub
    public static string Label(string value, string parameterName)
    {
        ThrowIf(
            invalid: string.IsNullOrWhiteSpace(value),
            parameterName: parameterName,
            rule: "it must not be empty or whitespace"
        );

        ThrowIf(
            invalid: value.Contains(',', StringComparison.Ordinal) || GitRefFormat.ContainsControlCharacter(value),
            parameterName: parameterName,
            rule: "it must not contain ',' or control characters"
        );

        ThrowIf(
            invalid: HasMoreRunesThan(value: value, maximum: MAX_LABEL_LENGTH),
            parameterName: parameterName,
            rule: "it must be at most 50 characters long"
        );

        return value;
    }

    // A list filter is the same label as cfwf matches, which also refuses a quote or backslash in it
    public static string ListLabel(string value, string parameterName)
    {
        string label = Label(value: value, parameterName: parameterName);

        ThrowIf(
            invalid: label.AsSpan().ContainsAny('"', '\\'),
            parameterName: parameterName,
            rule: "it must not contain '\"' or '\\'"
        );

        return label;
    }

    // A history filter only has to be a label name that could be matched, so ',' '"' and '\' are allowed in it
    public static string LabelFilter(string value, string parameterName)
    {
        ThrowIf(
            invalid: string.IsNullOrWhiteSpace(value),
            parameterName: parameterName,
            rule: "it must not be empty or whitespace"
        );

        ThrowIf(
            invalid: GitRefFormat.ContainsControlCharacter(value),
            parameterName: parameterName,
            rule: "it must not contain control characters"
        );

        ThrowIf(
            invalid: HasMoreRunesThan(value: value, maximum: MAX_LABEL_LENGTH),
            parameterName: parameterName,
            rule: "it must be at most 50 characters long"
        );

        return value;
    }

    // The label is returned in its standard spelling, so a write never creates a near-duplicate label
    public static string StandardLabel(string value, string parameterName)
    {
        string label = Label(value: value, parameterName: parameterName);

        return StandardLabels.FindStandard(label)
            ?? throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it must be a standard label"
            );
    }

    // GitHub stores a label colour as six hex digits without '#', and returns it in lower case
    public static string LabelColour(string value, string parameterName)
    {
        ThrowIf(
            invalid: value.Length != LABEL_COLOUR_LENGTH || value.AsSpan().ContainsAnyExcept(HexDigits),
            parameterName: parameterName,
            rule: "it must be 6 hexadecimal digits without '#'"
        );

        return HexToLower(value);
    }

    public static string LabelDescription(string value, string parameterName)
    {
        ThrowIf(
            invalid: string.IsNullOrWhiteSpace(value),
            parameterName: parameterName,
            rule: "it must not be empty or whitespace"
        );

        ThrowIf(
            invalid: GitRefFormat.ContainsControlCharacter(value),
            parameterName: parameterName,
            rule: "it must not contain control characters"
        );

        ThrowIf(
            invalid: HasMoreRunesThan(value: value, maximum: MAX_LABEL_DESCRIPTION_LENGTH),
            parameterName: parameterName,
            rule: "it must be at most 100 characters long"
        );

        return value;
    }

    public static string Login(string value, string parameterName)
    {
        ThrowIf(
            invalid: !IsLogin(value),
            parameterName: parameterName,
            rule: "it may only contain letters, digits, '_' and '-', optionally followed by '[bot]'"
        );

        return value;
    }

    public static bool IsLogin(string value)
    {
        ReadOnlySpan<char> name = value.EndsWith(value: BOT_SUFFIX, comparisonType: StringComparison.Ordinal)
            ? value.AsSpan(0, value.Length - BOT_SUFFIX.Length)
            : value.AsSpan();

        return !name.IsEmpty && !name.ContainsAnyExcept(LoginCharacters);
    }

    public static string Owner(string value, string parameterName)
    {
        ThrowIf(
            invalid: value.Length == 0 || value[0] == '-' || value.AsSpan().ContainsAnyExcept(OwnerCharacters),
            parameterName: parameterName,
            rule: "it may only contain letters, digits and '-' and must not start with '-'"
        );

        return value;
    }

    public static string GhsaId(string value, string parameterName)
    {
        ThrowIf(
            invalid: !IsGhsaId(value),
            parameterName: parameterName,
            rule: "it must be a GHSA id in the form GHSA-xxxx-xxxx-xxxx, each x one of 2-9, c, f, g, h, j, m, p, q, r, v, w or x"
        );

        return value;
    }

    // The GHSA id format ^GHSA(-[23456789cfghjmpqrvwx]{4}){3}$, matched over the whole value so a trailing newline is
    // refused, as a regex '$' would not
    public static bool IsGhsaId(string value)
    {
        if (
            value.Length != GHSA_ID_LENGTH
            || !value.StartsWith(value: GHSA_PREFIX, comparisonType: StringComparison.Ordinal)
        )
        {
            return false;
        }

        ReadOnlySpan<char> groups = value.AsSpan(GHSA_PREFIX.Length);

        for (int group = 0; group < GHSA_GROUPS; ++group)
        {
            ReadOnlySpan<char> part = groups.Slice(start: group * GHSA_GROUP_LENGTH, length: GHSA_GROUP_LENGTH);

            if (part[0] != '-' || part[1..].ContainsAnyExcept(GhsaCharacters))
            {
                return false;
            }
        }

        return true;
    }

    private static void ThrowIf(bool invalid, string parameterName, string rule)
    {
        if (invalid)
        {
            throw ParameterValidationException.For(parameterName: parameterName, rule: rule);
        }
    }

    private static string HexToLower(string value)
    {
        return string.Create(length: value.Length, state: value, action: static (span, source) =>
            Ascii.ToLower(source: source, destination: span, out _)
        );
    }

    // Every rune is at least one UTF-16 char, so a string no longer than the maximum cannot exceed it in runes
    private static bool HasMoreRunesThan(string value, int maximum)
    {
        if (value.Length <= maximum)
        {
            return false;
        }

        int count = 0;

        foreach (Rune rune in value.EnumerateRunes())
        {
            ++count;

            if (count > maximum)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRepositoryPart(ReadOnlySpan<char> part)
    {
        return !part.IsEmpty && !part.ContainsAnyExcept(RepositoryNameCharacters) && part is not "." and not "..";
    }

    private static bool IsInvalidPathPart(string part)
    {
        return part.Length == 0
            || StringComparer.Ordinal.Equals(x: part, y: ".")
            || StringComparer.Ordinal.Equals(x: part, y: "..");
    }
}
