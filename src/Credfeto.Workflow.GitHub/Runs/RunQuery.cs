using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;

namespace Credfeto.Workflow.GitHub.Runs;

public static class RunQuery
{
    public const int DEFAULT_LIMIT = 20;
    public const int MAX_LIMIT = 50;
    public const int DEFAULT_LINES = 200;
    public const int MAX_LINES = 2000;
    public const int DEFAULT_CONTEXT = 0;
    public const int MAX_CONTEXT = 20;
    public const int DEFAULT_MAX_MATCHES = 20;
    public const int MAX_MAX_MATCHES = 200;
    public const int DEFAULT_FROM_LINE = 1;
    public const int MAX_GREP_LENGTH = 256;

    private const int MAX_WORKFLOW_LENGTH = 256;
    private const int GREP_TIMEOUT_MILLISECONDS = 1000;

    private const RegexOptions GREP_OPTIONS = RegexOptions.CultureInvariant;

    public static int Limit(int value, string parameterName)
    {
        return InRange(value: value, min: 1, max: MAX_LIMIT, parameterName: parameterName);
    }

    public static int Lines(int value, string parameterName)
    {
        return InRange(value: value, min: 1, max: MAX_LINES, parameterName: parameterName);
    }

    public static int Context(int value, string parameterName)
    {
        return InRange(value: value, min: 0, max: MAX_CONTEXT, parameterName: parameterName);
    }

    public static int MaxMatches(int value, string parameterName)
    {
        return InRange(value: value, min: 1, max: MAX_MAX_MATCHES, parameterName: parameterName);
    }

    public static int FromLine(int value, string parameterName)
    {
        return value >= 1
            ? value
            : throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it must be a whole number of at least 1"
            );
    }

    // A pattern is matched against every line of a log, so it runs without backtracking where it can, which keeps
    // each match linear in the line's length; one that needs backtracking, such as a backreference, is bounded by
    // the match timeout instead
    public static Regex Grep(string value, string parameterName)
    {
        if (value.Length is 0 or > MAX_GREP_LENGTH)
        {
            throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: string.Create(
                    provider: CultureInfo.InvariantCulture,
                    $"it must be from 1 to {MAX_GREP_LENGTH} characters long"
                )
            );
        }

        try
        {
            return GrepRegex(value: value, options: GREP_OPTIONS | RegexOptions.NonBacktracking);
        }
        catch (NotSupportedException)
        {
            return GrepRegex(value: value, options: GREP_OPTIONS);
        }
        catch (ArgumentException exception)
        {
            throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it must be a valid regular expression",
                innerException: exception
            );
        }
    }

    private static Regex GrepRegex(string value, RegexOptions options)
    {
        return new(
            pattern: value,
            options: options,
            matchTimeout: TimeSpan.FromMilliseconds(GREP_TIMEOUT_MILLISECONDS)
        );
    }

    private static int InRange(int value, int min, int max, string parameterName)
    {
        return value >= min && value <= max
            ? value
            : throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: string.Create(
                    provider: CultureInfo.InvariantCulture,
                    $"it must be a whole number from {min} to {max}"
                )
            );
    }

    // A workflow is matched by its file name or its name, so it is never put into a URL
    public static string Workflow(string value, string parameterName)
    {
        if (value.Length == 0 || value[0] == '-' || GitRefFormat.ContainsControlCharacter(value))
        {
            throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it must not be empty, start with '-' or contain control characters"
            );
        }

        // Length is counted in Unicode scalar values (runes) so an emoji counts once
        if (value.Length > MAX_WORKFLOW_LENGTH && value.EnumerateRunes().Skip(MAX_WORKFLOW_LENGTH).Any())
        {
            throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it must be at most 256 characters long"
            );
        }

        return value;
    }

    // Run and job ids are digits only; the parsed value is what goes into a URL, so leading zeros cannot change it
    public static long Id(string value, string parameterName)
    {
        string number = GitHubNames.PositiveNumber(value: value, parameterName: parameterName);

        return long.TryParse(s: number, style: NumberStyles.None, provider: CultureInfo.InvariantCulture, out long id)
            ? id
            : throw ParameterValidationException.For(parameterName: parameterName, rule: "it is too large");
    }
}
