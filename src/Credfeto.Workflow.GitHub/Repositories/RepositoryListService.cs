using System;
using System.Collections.Generic;
using System.Linq;
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

public sealed class RepositoryListService : IRepositoryListService
{
    private const int MAX_REPOSITORIES = 1000;

    private const int PAGE_SIZE = 100;

    private const int MAX_PAGES = MAX_REPOSITORIES / PAGE_SIZE;

    // repositoryOwner resolves both users and organisations, and ownerAffiliations keeps it to the owner's own
    // repositories, as gh repo list does
    private const string REPOSITORIES_QUERY =
        "query($o:String!,$c:String,$a:Boolean){repositoryOwner(login:$o){repositories(first:100,after:$c,isArchived:$a,ownerAffiliations:[OWNER],orderBy:{field:NAME,direction:ASC}){totalCount pageInfo{hasNextPage endCursor} nodes{nameWithOwner visibility isArchived defaultBranchRef{name}}}}}";

    private readonly IGitHubGraphQlClient _graphQlClient;

    public RepositoryListService(IGitHubGraphQlClient graphQlClient)
    {
        this._graphQlClient = graphQlClient;
    }

    public ValueTask<RepositoryListResponse> ListAsync(
        string owner,
        bool includeArchived,
        CancellationToken cancellationToken
    )
    {
        string validOwner = GitHubNames.Owner(value: owner, parameterName: nameof(owner));

        return this.ReadAsync(
            validOwner: validOwner,
            archived: includeArchived ? null : false,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<RepositoryListResponse> ReadAsync(
        string validOwner,
        bool? archived,
        CancellationToken cancellationToken
    )
    {
        try
        {
            IReadOnlyList<RepositoryListNode?> nodes = await this.ReadAllPagesAsync(
                validOwner: validOwner,
                archived: archived,
                cancellationToken: cancellationToken
            );

            return new(
                Owner: validOwner,
                Repositories:
                [
                    .. nodes
                        .Select(RepositoryListProjector.Project)
                        .OrderBy(keySelector: entry => entry.Repository, comparer: StringComparer.OrdinalIgnoreCase)
                        .ThenBy(keySelector: entry => entry.Repository, comparer: StringComparer.Ordinal),
                ]
            );
        }
        catch (Exception exception)
            when (exception is GitHubGraphQlException or GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new RepositoryListFailedException(
                message: $"could not list the repositories of {validOwner}",
                innerException: exception
            );
        }
    }

    private async ValueTask<IReadOnlyList<RepositoryListNode?>> ReadAllPagesAsync(
        string validOwner,
        bool? archived,
        CancellationToken cancellationToken
    )
    {
        List<RepositoryListNode?> nodes = [];
        HashSet<string> seenCursors = [with(StringComparer.Ordinal)];
        string? after = null;

        for (int page = 0; page < MAX_PAGES; ++page)
        {
            RepositoryListQueryData data = await this._graphQlClient.ExecuteAsync(
                query: REPOSITORIES_QUERY,
                variables: new RepositoryListQueryVariables(Owner: validOwner, After: after, Archived: archived),
                variablesTypeInfo: GitHubJsonContext.Default.RepositoryListQueryVariables,
                dataTypeInfo: GitHubJsonContext.Default.RepositoryListQueryData,
                cancellationToken: cancellationToken
            );

            GraphQlConnection<RepositoryListNode> connection = Connection(data: data, validOwner: validOwner);

            nodes.AddRange(
                connection.Nodes
                    ?? throw new GitHubInvalidResponseException("GitHub returned a page of repositories without nodes")
            );

            if (connection.PageInfo is not { HasNextPage: bool hasNextPage })
            {
                throw new GitHubInvalidResponseException("GitHub returned a page of repositories without page info");
            }

            if (!hasNextPage)
            {
                return nodes;
            }

            // The cursor comes from the upstream, so a missing or repeated one must fail rather than loop forever
            string? cursor = connection.PageInfo.EndCursor;

            if (!GraphQlCursor.IsValid(cursor) || !seenCursors.Add(cursor))
            {
                throw new GitHubInvalidResponseException("GitHub returned an invalid repositories page cursor");
            }

            after = cursor;
        }

        throw new GitHubInvalidResponseException("GitHub returned more pages of repositories than it counted");
    }

    private static GraphQlConnection<RepositoryListNode> Connection(RepositoryListQueryData data, string validOwner)
    {
        RepositoryListQueryOwner owner =
            data.RepositoryOwner
            ?? throw new OwnerNotFoundException($"the owner {validOwner} was not found, or the caller cannot see it");

        GraphQlConnection<RepositoryListNode> connection =
            owner.Repositories
            ?? throw new GitHubInvalidResponseException("GitHub returned an owner without repositories");

        int totalCount =
            connection.TotalCount
            ?? throw new GitHubInvalidResponseException("GitHub returned repositories without a total count");

        // Listing only part of the repositories would silently hide the rest, so too many is refused
        if (totalCount > MAX_REPOSITORIES)
        {
            throw new RepositoryListRefusedException(
                $"{validOwner} has more than {MAX_REPOSITORIES} repositories to list"
            );
        }

        return connection;
    }
}
