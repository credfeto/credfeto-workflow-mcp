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
    private const string BOT_SUFFIX = "[bot]";

    private static readonly SearchValues<char> RepositoryNameCharacters = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_.-"
    );

    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    private static readonly SearchValues<char> LoginCharacters = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-"
    );

    public static string Repository(string value, string parameterName)
    {
        int separator = value.IndexOf('/', StringComparison.Ordinal);

        if (separator < 0 || value.AsSpan(separator + 1).Contains('/'))
        {
            throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it must be in the form owner/repo"
            );
        }

        if (!IsRepositoryPart(value.AsSpan(0, separator)) || !IsRepositoryPart(value.AsSpan(separator + 1)))
        {
            throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "owner and repo may only contain letters, digits, '_', '.' and '-' and must not be '.' or '..'"
            );
        }

        return value;
    }

    public static string Number(string value, string parameterName)
    {
        if (value.Length == 0 || value.AsSpan().ContainsAnyExceptInRange(lowInclusive: '0', highInclusive: '9'))
        {
            throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it must contain only the digits 0-9"
            );
        }

        string trimmed = value.TrimStart('0');

        return trimmed.Length == 0 ? "0" : trimmed;
    }

    public static string PositiveNumber(string value, string parameterName)
    {
        string number = Number(value: value, parameterName: parameterName);

        if (StringComparer.Ordinal.Equals(x: number, y: "0"))
        {
            throw ParameterValidationException.For(parameterName: parameterName, rule: "it must be greater than zero");
        }

        return number;
    }

    // The digit limit applies after leading zeros are removed, so it bounds the numeric value rather than the input text
    public static string CommentId(string value, string parameterName)
    {
        string number = PositiveNumber(value: value, parameterName: parameterName);

        if (number.Length > MAX_COMMENT_ID_DIGITS)
        {
            throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it must have at most 19 significant digits"
            );
        }

        return number;
    }

    public static string CommitSha(string value, string parameterName)
    {
        if (value.Length != COMMIT_SHA_LENGTH || value.AsSpan().ContainsAnyExcept(HexDigits))
        {
            throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it must be a full 40 character hexadecimal commit SHA"
            );
        }

        return string.Create(
            length: value.Length,
            state: value,
            action: static (span, source) => Ascii.ToLower(source: source, destination: span, out _)
        );
    }

    public static string Branch(string value, string parameterName)
    {
        Tag(value: value, parameterName: parameterName);

        if (value[0] == '-' || StringComparer.Ordinal.Equals(x: value, y: "HEAD"))
        {
            throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "a branch name must not start with '-' or be 'HEAD'"
            );
        }

        return value;
    }

    public static string Tag(string value, string parameterName)
    {
        if (!GitRefFormat.IsValidRefName(value))
        {
            throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it must be a valid git ref name"
            );
        }

        return value;
    }

    public static string Status(string value, string parameterName)
    {
        if (GitRefFormat.ContainsControlCharacter(value))
        {
            throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it must not contain control characters"
            );
        }

        return value;
    }

    // Length is counted in Unicode scalar values (runes) so an emoji counts once, as it does on GitHub
    public static string Label(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it must not be empty or whitespace"
            );
        }

        if (value.Contains(',', StringComparison.Ordinal) || GitRefFormat.ContainsControlCharacter(value))
        {
            throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it must not contain ',' or control characters"
            );
        }

        if (value.EnumerateRunes().Skip(MAX_LABEL_LENGTH).Any())
        {
            throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it must be at most 50 characters long"
            );
        }

        return value;
    }

    public static string Login(string value, string parameterName)
    {
        ReadOnlySpan<char> name = value.EndsWith(value: BOT_SUFFIX, comparisonType: StringComparison.Ordinal)
            ? value.AsSpan(0, value.Length - BOT_SUFFIX.Length)
            : value.AsSpan();

        if (name.IsEmpty || name.ContainsAnyExcept(LoginCharacters))
        {
            throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it may only contain letters, digits, '_' and '-', optionally followed by '[bot]'"
            );
        }

        return value;
    }

    private static bool IsRepositoryPart(ReadOnlySpan<char> part)
    {
        return !part.IsEmpty && !part.ContainsAnyExcept(RepositoryNameCharacters) && part is not "." and not "..";
    }
}
