using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Leaks;

public sealed class RepositoryVisibilityLookup : IRepositoryVisibilityLookup
{
    private const string VISIBILITY_QUERY = "query($o:String!,$r:String!){repository(owner:$o,name:$r){visibility}}";

    private const string REPOSITORY_NOT_FOUND = "Could not resolve to a Repository";

    private readonly IGitHubGraphQlClient _graphQlClient;

    public RepositoryVisibilityLookup(IGitHubGraphQlClient graphQlClient)
    {
        this._graphQlClient = graphQlClient;
    }

    public async ValueTask<RepositoryVisibility> GetVisibilityAsync(
        string repository,
        CancellationToken cancellationToken
    )
    {
        string validated = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        int separator = validated.IndexOf('/', StringComparison.Ordinal);

        try
        {
            VisibilityQueryData data = await this._graphQlClient.ExecuteAsync(
                query: VISIBILITY_QUERY,
                variables: new VisibilityQueryVariables(
                    Owner: validated[..separator],
                    Name: validated[(separator + 1)..]
                ),
                variablesTypeInfo: GitHubJsonContext.Default.VisibilityQueryVariables,
                dataTypeInfo: GitHubJsonContext.Default.VisibilityQueryData,
                cancellationToken: cancellationToken
            );

            return data.Repository is null ? RepositoryVisibility.NOT_FOUND : ToVisibility(data.Repository.Visibility);
        }
        catch (GitHubGraphQlException exception) when (IsNotFound(exception))
        {
            return RepositoryVisibility.NOT_FOUND;
        }
        catch (Exception exception)
            when (exception is GitHubGraphQlException or GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream message can name the repository, so only a fixed message is surfaced
            throw new RepositoryVisibilityLookupFailedException(
                message: "the visibility of a referenced repository could not be read",
                innerException: exception
            );
        }
    }

    private static bool IsNotFound(GitHubGraphQlException exception)
    {
        return exception.Errors.Any(error =>
            error.Contains(value: REPOSITORY_NOT_FOUND, comparisonType: StringComparison.Ordinal)
        );
    }

    // A visibility GitHub adds later is treated as not public, so an unknown value can never leak a reference
    private static RepositoryVisibility ToVisibility(string? visibility)
    {
        return visibility switch
        {
            "PUBLIC" => RepositoryVisibility.PUBLIC,
            "INTERNAL" => RepositoryVisibility.INTERNAL,
            _ => RepositoryVisibility.PRIVATE,
        };
    }
}
