using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.PullRequests;

namespace Credfeto.Workflow.GitHub.ReleaseView;

public sealed class ReleaseService : IReleaseService
{
    private const int MAX_RELEASES = 1000;

    private const int PAGE_SIZE = 100;

    private const string REPOSITORY_NOT_FOUND = "Could not resolve to a Repository";

    // The release notes are never requested; GitHub's isLatest is only on GraphQL, not on the REST release list
    private const string RELEASES_QUERY =
        "query($o:String!,$r:String!,$c:String){repository(owner:$o,name:$r){releases(first:100,after:$c,orderBy:{field:CREATED_AT,direction:DESC}){totalCount pageInfo{hasNextPage endCursor} nodes{tagName name isDraft isPrerelease isLatest publishedAt}}}}";

    private readonly IGitHubGraphQlClient _graphQlClient;
    private readonly IGitHubRestClient _restClient;

    public ReleaseService(IGitHubRestClient restClient, IGitHubGraphQlClient graphQlClient)
    {
        this._restClient = restClient;
        this._graphQlClient = graphQlClient;
    }

    // The repository is validated before anything is read, so an invalid one is reported as such rather than as a
    // failed read
    public ValueTask<ReleaseViewResult> ViewLatestAsync(string repository, CancellationToken cancellationToken)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        return this.ViewOrFailAsync(repository: validRepository, cancellationToken: cancellationToken);
    }

    private async ValueTask<ReleaseViewResult> ViewOrFailAsync(string repository, CancellationToken cancellationToken)
    {
        try
        {
            return Project(await this.ReadLatestAsync(repository: repository, cancellationToken: cancellationToken));
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new ReleaseViewFailedException(
                message: $"could not read the latest release of {repository}",
                innerException: exception
            );
        }
    }

    private async ValueTask<GitHubRelease> ReadLatestAsync(string repository, CancellationToken cancellationToken)
    {
        try
        {
            return await this._restClient.GetAsync(
                relativeUri: $"repos/{repository}/releases/latest",
                responseTypeInfo: GitHubJsonContext.Default.GitHubRelease,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw await this.MissingReleaseOrRepositoryAsync(
                repository: repository,
                releaseNotFound: exception,
                cancellationToken: cancellationToken
            );
        }
    }

    // A repository without a latest release and a missing or hidden repository both answer 404, so the repository is
    // read to tell them apart
    private async ValueTask<Exception> MissingReleaseOrRepositoryAsync(
        string repository,
        HttpRequestException releaseNotFound,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await this._restClient.GetAsync(
                relativeUri: $"repos/{repository}",
                responseTypeInfo: GitHubJsonContext.Default.RepositoryResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return new RepositoryNotFoundException(
                message: $"the repository {repository} was not found, or the caller cannot see it",
                innerException: exception
            );
        }

        return new ReleaseNotFoundException(
            message: $"{repository} has no latest release",
            innerException: releaseNotFound
        );
    }

    public ValueTask<ReleaseListResponse> ListAsync(string repository, CancellationToken cancellationToken)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        return this.ListOrFailAsync(repository: validRepository, cancellationToken: cancellationToken);
    }

    private async ValueTask<ReleaseListResponse> ListOrFailAsync(string repository, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<ReleaseSummary> releases = await this.ReadAllPagesAsync(
                repository: repository,
                cancellationToken: cancellationToken
            );

            // Newest first by time, never by version, so a backport published after the latest release sits above it
            return new(
                Repository: repository,
                Releases:
                [
                    .. releases
                        .OrderByDescending(
                            keySelector: release => release.PublishedAt,
                            comparer: StringComparer.Ordinal
                        )
                        .ThenByDescending(keySelector: release => release.Tag, comparer: StringComparer.Ordinal),
                ]
            );
        }
        catch (GitHubGraphQlException exception) when (IsRepositoryNotFound(exception))
        {
            throw RepositoryNotFound(repository: repository, innerException: exception);
        }
        catch (Exception exception)
            when (exception is GitHubGraphQlException or GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new ReleaseListFailedException(
                message: $"could not list the releases of {repository}",
                innerException: exception
            );
        }
    }

    private async ValueTask<IReadOnlyList<ReleaseSummary>> ReadAllPagesAsync(
        string repository,
        CancellationToken cancellationToken
    )
    {
        ReleaseListQueryVariables first = FirstPage(repository);
        List<ReleaseSummary> releases = [];
        HashSet<string> seenCursors = [with(StringComparer.Ordinal)];
        ReleaseListQueryVariables variables = first;
        int? pageLimit = null;

        for (int page = 0; page < (pageLimit ?? 1); ++page)
        {
            GraphQlConnection<ReleaseListNode> connection = await this.ReadPageAsync(
                variables: variables,
                repository: repository,
                cancellationToken: cancellationToken
            );

            // The count includes drafts, which are not listed, so it only bounds how many pages can be read
            pageLimit ??= PageLimit(connection);

            releases.AddRange(Published(connection));

            // Listing only part of the releases would silently hide the rest, so too many is refused
            if (releases.Count > MAX_RELEASES)
            {
                throw new ReleaseListRefusedException(
                    $"{repository} has more than {MAX_RELEASES} releases, so none is printed"
                );
            }

            string? cursor = NextCursor(connection: connection, seenCursors: seenCursors);

            if (cursor is null)
            {
                return releases;
            }

            variables = first with { After = cursor };
        }

        throw new GitHubInvalidResponseException("GitHub returned more pages of releases than it counted");
    }

    private static ReleaseListQueryVariables FirstPage(string repository)
    {
        // GitHubNames.Repository guarantees exactly one separator between a non-empty owner and name
        int separator = repository.IndexOf('/', StringComparison.Ordinal);

        return new(Owner: repository[..separator], Name: repository[(separator + 1)..], After: null);
    }

    private async ValueTask<GraphQlConnection<ReleaseListNode>> ReadPageAsync(
        ReleaseListQueryVariables variables,
        string repository,
        CancellationToken cancellationToken
    )
    {
        ReleaseListQueryData data = await this._graphQlClient.ExecuteAsync(
            query: RELEASES_QUERY,
            variables: variables,
            variablesTypeInfo: GitHubJsonContext.Default.ReleaseListQueryVariables,
            dataTypeInfo: GitHubJsonContext.Default.ReleaseListQueryData,
            cancellationToken: cancellationToken
        );

        return Connection(data: data, repository: repository);
    }

    private static IEnumerable<ReleaseSummary> Published(GraphQlConnection<ReleaseListNode> connection)
    {
        return ReleaseListProjector.Published(
            connection.Nodes
                ?? throw new GitHubInvalidResponseException("GitHub returned a page of releases without nodes")
        );
    }

    private static string? NextCursor(GraphQlConnection<ReleaseListNode> connection, HashSet<string> seenCursors)
    {
        if (connection.PageInfo is not { HasNextPage: bool hasNextPage })
        {
            throw new GitHubInvalidResponseException("GitHub returned a page of releases without page info");
        }

        if (!hasNextPage)
        {
            return null;
        }

        // The cursor comes from the upstream, so a missing or repeated one must fail rather than loop forever
        string? cursor = connection.PageInfo.EndCursor;

        if (!GraphQlCursor.IsValid(cursor) || !seenCursors.Add(cursor))
        {
            throw new GitHubInvalidResponseException("GitHub returned an invalid releases page cursor");
        }

        return cursor;
    }

    private static GraphQlConnection<ReleaseListNode> Connection(ReleaseListQueryData data, string repository)
    {
        ReleaseListQueryRepository found = data.Repository
            ?? throw RepositoryNotFound(repository: repository, innerException: null);

        return found.Releases
            ?? throw new GitHubInvalidResponseException("GitHub returned a repository without releases");
    }

    private static int PageLimit(GraphQlConnection<ReleaseListNode> connection)
    {
        int totalCount = connection.TotalCount
            ?? throw new GitHubInvalidResponseException("GitHub returned releases without a total count");

        return totalCount / PAGE_SIZE + 1;
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

    private static ReleaseViewResult Project(GitHubRelease release)
    {
        if (release.TagName is not { Length: > 0 } tag)
        {
            throw new GitHubInvalidResponseException("GitHub returned a release without a tag");
        }

        return new(
            Tag: UpstreamText.ReplaceLineBreaking(tag),
            PublishedAt: Field(release.PublishedAt),
            Url: ReleaseUrl(release.HtmlUrl),
            Name: Field(release.Name)
        );
    }

    private static Uri ReleaseUrl(string? htmlUrl)
    {
        return
            htmlUrl is not null
            && Uri.TryCreate(uriString: htmlUrl, uriKind: UriKind.Absolute, out Uri? url)
            && UriSchemes.IsHttp(url)
            ? url
            : throw new GitHubInvalidResponseException("GitHub returned a release without a valid link");
    }

    private static string Field(string? value)
    {
        return value is null ? string.Empty : UpstreamText.ReplaceLineBreaking(value);
    }
}
