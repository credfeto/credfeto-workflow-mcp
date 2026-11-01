using System;
using System.Buffers;
using System.Text;

namespace Credfeto.Workflow.GitHub.Interfaces.Validation;

public static class GitHubNames
{
    private const int MAX_COMMENT_ID_DIGITS = 19;
    private const int COMMIT_SHA_LENGTH = 40;
    private const int MAX_LABEL_LENGTH = 50;
    private const string BOT_SUFFIX = "[bot]";

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

        return string.Create(
            length: value.Length,
            state: value,
            action: static (span, source) => Ascii.ToLower(source: source, destination: span, out _)
        );
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

    public static string Login(string value, string parameterName)
    {
        ReadOnlySpan<char> name = value.EndsWith(value: BOT_SUFFIX, comparisonType: StringComparison.Ordinal)
            ? value.AsSpan(0, value.Length - BOT_SUFFIX.Length)
            : value.AsSpan();

        ThrowIf(
            invalid: name.IsEmpty || name.ContainsAnyExcept(LoginCharacters),
            parameterName: parameterName,
            rule: "it may only contain letters, digits, '_' and '-', optionally followed by '[bot]'"
        );

        return value;
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

    private static void ThrowIf(bool invalid, string parameterName, string rule)
    {
        if (invalid)
        {
            throw ParameterValidationException.For(parameterName: parameterName, rule: rule);
        }
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
}
