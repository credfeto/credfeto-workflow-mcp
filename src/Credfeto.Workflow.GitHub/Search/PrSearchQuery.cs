using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;

namespace Credfeto.Workflow.GitHub.Search;

public static class PrSearchQuery
{
    public static PrSearchState State(string? value, string parameterName)
    {
        return value switch
        {
            null or "" or "open" => PrSearchState.OPEN,
            "closed" => PrSearchState.CLOSED,
            "merged" => PrSearchState.MERGED,
            "all" => PrSearchState.ALL,
            _ => throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "it must be open, closed, merged or all"
            ),
        };
    }

    // The repository and type qualifiers are always added here, after the words, so the caller's text can never
    // choose them
    public static string Build(string repository, string words, PrSearchState state)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        string query = $"in:title,body {words} repo:{validRepository} type:pr";

        return state switch
        {
            PrSearchState.OPEN => query + " state:open",
            PrSearchState.CLOSED => query + " state:closed",
            PrSearchState.MERGED => query + " is:merged",
            _ => query,
        };
    }
}
