using Credfeto.Workflow.GitHub.Interfaces.Validation;

namespace Credfeto.Workflow.GitHub.Search;

public static class CodeSearchQuery
{
    // The repository qualifier is always added here, after the words, so the caller's text can never choose it
    public static string Build(string repository, string words)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        return $"{words} repo:{validRepository}";
    }
}
