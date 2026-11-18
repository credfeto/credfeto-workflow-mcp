using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.PullRequests;

namespace Credfeto.Workflow.GitHub.Repositories;

public sealed class RepoViewService : IRepoViewService
{
    private const string REPOSITORY_NOT_FOUND = "Could not resolve to a Repository";

    // GraphQL rather than REST, because REST answers the merge methods as null to a caller without push access
    private const string VIEW_QUERY =
        "query($o:String!,$r:String!){repository(owner:$o,name:$r){defaultBranchRef{name} description visibility mergeCommitAllowed squashMergeAllowed rebaseMergeAllowed}}";

    private readonly IGitHubGraphQlClient _graphQlClient;

    public RepoViewService(IGitHubGraphQlClient graphQlClient)
    {
        this._graphQlClient = graphQlClient;
    }

    // The repository is validated before anything is read, so an invalid one is reported as such rather than as a
    // failed read
    public ValueTask<RepoViewResult> ViewAsync(string repository, CancellationToken cancellationToken)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        return this.ViewOrFailAsync(repository: validRepository, cancellationToken: cancellationToken);
    }

    private async ValueTask<RepoViewResult> ViewOrFailAsync(string repository, CancellationToken cancellationToken)
    {
        try
        {
            RepoViewQueryData data = await this._graphQlClient.ExecuteAsync(
                query: VIEW_QUERY,
                variables: Variables(repository),
                variablesTypeInfo: GitHubJsonContext.Default.RepoViewQueryVariables,
                dataTypeInfo: GitHubJsonContext.Default.RepoViewQueryData,
                cancellationToken: cancellationToken
            );

            return Project(data.Repository ?? throw RepositoryNotFound(repository: repository, innerException: null));
        }
        catch (GitHubGraphQlException exception) when (IsRepositoryNotFound(exception))
        {
            throw RepositoryNotFound(repository: repository, innerException: exception);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw RepositoryNotFound(repository: repository, innerException: exception);
        }
        catch (Exception exception)
            when (exception is GitHubGraphQlException or GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new RepoViewFailedException(
                message: $"could not view the repository {repository}",
                innerException: exception
            );
        }
    }

    private static RepoViewQueryVariables Variables(string repository)
    {
        // GitHubNames.Repository guarantees exactly one separator between a non-empty owner and name
        int separator = repository.IndexOf('/', StringComparison.Ordinal);

        return new(Owner: repository[..separator], Name: repository[(separator + 1)..]);
    }

    private static RepoViewResult Project(RepoViewRepository found)
    {
        return new(
            DefaultBranch: PullRequestViewText.Clean(found.DefaultBranchRef?.Name),
            Visibility: Visibility(found.Visibility),
            AllowMergeCommit: found.MergeCommitAllowed,
            AllowSquashMerge: found.SquashMergeAllowed,
            AllowRebaseMerge: found.RebaseMergeAllowed,
            Description: PullRequestViewText.Clean(found.Description)
        );
    }

    // Only GitHub's own enum shape is accepted, so an upstream value can never smuggle free text into the answer
    private static string Visibility(string? visibility)
    {
        return !string.IsNullOrEmpty(visibility) && visibility.All(IsVisibilityCharacter)
            ? visibility
            : throw new GitHubInvalidResponseException("GitHub returned a repository with an invalid visibility");
    }

    private static bool IsVisibilityCharacter(char character)
    {
        return character is >= 'A' and <= 'Z' or '_';
    }

    private static bool IsRepositoryNotFound(GitHubGraphQlException exception)
    {
        return exception.Errors.Any(static error =>
            error.Contains(value: REPOSITORY_NOT_FOUND, comparisonType: StringComparison.Ordinal)
        );
    }

    private static RepositoryNotFoundException RepositoryNotFound(string repository, Exception? innerException)
    {
        return new(
            message: $"the repository {repository} was not found, or the caller cannot see it",
            innerException: innerException
        );
    }
}
