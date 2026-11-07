using Credfeto.Workflow.GitHub.Interfaces.Validation;

namespace Credfeto.Workflow.GitHub.Search;

public static class RepoSearchQuery
{
    // The owner and archive qualifiers are always added here, after the words, so the caller's text can never choose
    // them; the user qualifier matches organisations as well as users
    public static string Build(string owner, string words, bool includeArchived)
    {
        string validOwner = GitHubNames.Owner(value: owner, parameterName: nameof(owner));
        string query = $"in:name,description {words} user:{validOwner}";

        return includeArchived ? query : query + " archived:false";
    }
}
