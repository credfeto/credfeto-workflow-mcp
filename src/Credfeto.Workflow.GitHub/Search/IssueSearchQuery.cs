using System.Globalization;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;

namespace Credfeto.Workflow.GitHub.Search;

public static class IssueSearchQuery
{
    public const int DEFAULT_LIMIT = 30;
    public const int MAX_LIMIT = 100;

    private const int MAX_LIMIT_DIGITS = 3;
    private const string LIMIT_RULE = "it must be a whole number from 1 to 100";

    public static IssueSearchState State(string? value, string parameterName)
    {
        return value switch
        {
            null or "" or "open" => IssueSearchState.OPEN,
            "closed" => IssueSearchState.CLOSED,
            "all" => IssueSearchState.ALL,
            _ => throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it must be open, closed or all"
            ),
        };
    }

    // The length is checked before the value is parsed, so a huge number is refused rather than overflowing
    public static int Limit(string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value))
        {
            return DEFAULT_LIMIT;
        }

        string number = GitHubNames.Number(value: value, parameterName: parameterName);

        int limit =
            number.Length > MAX_LIMIT_DIGITS
                ? 0
                : int.Parse(s: number, style: NumberStyles.None, provider: CultureInfo.InvariantCulture);

        return Limit(value: limit, parameterName: parameterName);
    }

    public static int Limit(int value, string parameterName)
    {
        return value is >= 1 and <= MAX_LIMIT
            ? value
            : throw ParameterValidationException.For(parameterName: parameterName, rule: LIMIT_RULE);
    }

    // The repository and type qualifiers are always added here, after the words, so the caller's text can never
    // choose them
    public static string Build(string repository, string words, IssueSearchState state)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        string query = $"in:title,body {words} repo:{validRepository} type:issue";

        return state switch
        {
            IssueSearchState.OPEN => query + " state:open",
            IssueSearchState.CLOSED => query + " state:closed",
            _ => query,
        };
    }
}
