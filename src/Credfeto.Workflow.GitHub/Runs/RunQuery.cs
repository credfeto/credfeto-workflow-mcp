using System.Globalization;
using System.Linq;
using Credfeto.Workflow.GitHub.Interfaces.Validation;

namespace Credfeto.Workflow.GitHub.Runs;

public static class RunQuery
{
    public const int DEFAULT_LIMIT = 20;
    public const int MAX_LIMIT = 50;
    public const int DEFAULT_LINES = 200;
    public const int MAX_LINES = 2000;

    private const int MAX_WORKFLOW_LENGTH = 256;

    public static int Limit(int value, string parameterName)
    {
        return InRange(value: value, max: MAX_LIMIT, parameterName: parameterName);
    }

    public static int Lines(int value, string parameterName)
    {
        return InRange(value: value, max: MAX_LINES, parameterName: parameterName);
    }

    private static int InRange(int value, int max, string parameterName)
    {
        return value >= 1 && value <= max
            ? value
            : throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: string.Create(
                    provider: CultureInfo.InvariantCulture,
                    $"it must be a whole number from 1 to {max}"
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
