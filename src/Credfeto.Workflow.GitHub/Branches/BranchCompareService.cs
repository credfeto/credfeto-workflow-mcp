using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Branches;

public sealed class BranchCompareService : IBranchCompareService
{
    private const string BRANCH_REF_PREFIX = "refs/heads/";

    private const string NOT_FOUND_ERROR_TYPE = "NOT_FOUND";

    // GraphQL rather than the REST compare API, which also returns every changed file and its patch
    private const string COMPARE_QUERY =
        "query($o:String!,$r:String!,$b:String!,$h:String!){repository(owner:$o,name:$r){ref(qualifiedName:$b){compare(headRef:$h){status aheadBy behindBy}}}}";

    // A ref that does not exist is answered as null rather than as an error, so this tells the missing one apart
    private const string REFS_QUERY =
        "query($o:String!,$r:String!,$b:String!,$h:String!){repository(owner:$o,name:$r){base:ref(qualifiedName:$b){name} head:ref(qualifiedName:$h){name}}}";

    private readonly IGitHubGraphQlClient _graphQlClient;

    public BranchCompareService(IGitHubGraphQlClient graphQlClient)
    {
        this._graphQlClient = graphQlClient;
    }

    // The arguments are validated before anything is read, so an invalid one is reported as such rather than as a
    // failed read
    public ValueTask<BranchCompareResponse> CompareAsync(
        string repository,
        string baseBranch,
        string headBranch,
        CancellationToken cancellationToken
    )
    {
        CompareTarget target = new(
            Repository: GitHubNames.Repository(value: repository, parameterName: nameof(repository)),
            Base: GitHubNames.Branch(value: baseBranch, parameterName: "base"),
            Head: GitHubNames.Branch(value: headBranch, parameterName: "head")
        );

        return this.CompareOrFailAsync(target: target, cancellationToken: cancellationToken);
    }

    private async ValueTask<BranchCompareResponse> CompareOrFailAsync(
        CompareTarget target,
        CancellationToken cancellationToken
    )
    {
        try
        {
            BranchComparison comparison = await this.ReadComparisonAsync(
                target: target,
                cancellationToken: cancellationToken
            );

            return Project(target: target, comparison: comparison);
        }
        catch (Exception exception)
            when (exception is GitHubGraphQlException or GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new BranchCompareFailedException(
                message: $"could not compare {target.Base}...{target.Head} in {target.Repository}",
                innerException: exception
            );
        }
    }

    private async ValueTask<BranchComparison> ReadComparisonAsync(
        CompareTarget target,
        CancellationToken cancellationToken
    )
    {
        BranchCompareQueryData data;

        try
        {
            data = await this._graphQlClient.ExecuteAsync(
                query: COMPARE_QUERY,
                variables: Variables(target),
                variablesTypeInfo: GitHubJsonContext.Default.BranchCompareQueryVariables,
                dataTypeInfo: GitHubJsonContext.Default.BranchCompareQueryData,
                cancellationToken: cancellationToken
            );
        }
        catch (GitHubGraphQlException exception) when (IsNotFound(exception))
        {
            throw await this.MissingBranchOrRepositoryAsync(
                target: target,
                compareFailure: exception,
                cancellationToken: cancellationToken
            );
        }

        BranchCompareRepository found = data.Repository
            ?? throw RepositoryNotFound(target: target, innerException: null);
        BranchCompareRef baseRef = found.Ref
            ?? throw BranchNotFound(target: target, branch: target.Base, innerException: null);

        return baseRef.Compare ?? throw new GitHubInvalidResponseException("GitHub returned no comparison");
    }

    // A missing head branch and a missing or hidden repository are both errors to the compare query
    private async ValueTask<Exception> MissingBranchOrRepositoryAsync(
        CompareTarget target,
        GitHubGraphQlException compareFailure,
        CancellationToken cancellationToken
    )
    {
        BranchRefsQueryData data;

        try
        {
            data = await this._graphQlClient.ExecuteAsync(
                query: REFS_QUERY,
                variables: Variables(target),
                variablesTypeInfo: GitHubJsonContext.Default.BranchCompareQueryVariables,
                dataTypeInfo: GitHubJsonContext.Default.BranchRefsQueryData,
                cancellationToken: cancellationToken
            );
        }
        catch (GitHubGraphQlException exception) when (IsNotFound(exception))
        {
            return RepositoryNotFound(target: target, innerException: exception);
        }

        if (data.Repository is not { } found)
        {
            return RepositoryNotFound(target: target, innerException: compareFailure);
        }

        if (found.Base is null)
        {
            return BranchNotFound(target: target, branch: target.Base, innerException: compareFailure);
        }

        return found.Head is null
            ? BranchNotFound(target: target, branch: target.Head, innerException: compareFailure)
            : new GitHubInvalidResponseException(
                message: "GitHub could not compare two branches that both exist",
                innerException: compareFailure
            );
    }

    private static BranchCompareQueryVariables Variables(CompareTarget target)
    {
        // GitHubNames.Repository guarantees exactly one separator between a non-empty owner and name
        int separator = target.Repository.IndexOf('/', StringComparison.Ordinal);

        return new(
            Owner: target.Repository[..separator],
            Name: target.Repository[(separator + 1)..],
            BaseRef: BRANCH_REF_PREFIX + target.Base,
            HeadRef: BRANCH_REF_PREFIX + target.Head
        );
    }

    private static bool IsNotFound(GitHubGraphQlException exception)
    {
        return exception.ErrorTypes.Contains(value: NOT_FOUND_ERROR_TYPE, comparer: StringComparer.Ordinal);
    }

    private static BranchCompareResponse Project(CompareTarget target, BranchComparison comparison)
    {
        return new(
            Repo: target.Repository,
            Base: target.Base,
            Head: target.Head,
            Status: Status(comparison.Status),
            AheadBy: Count(comparison.AheadBy),
            BehindBy: Count(comparison.BehindBy)
        );
    }

    private static BranchCompareStatus Status(string? status)
    {
        return status switch
        {
            "IDENTICAL" => BranchCompareStatus.Identical,
            "AHEAD" => BranchCompareStatus.Ahead,
            "BEHIND" => BranchCompareStatus.Behind,
            "DIVERGED" => BranchCompareStatus.Diverged,
            _ => throw new GitHubInvalidResponseException("GitHub returned an unknown comparison status"),
        };
    }

    private static int Count(int? count)
    {
        return count is >= 0
            ? count.Value
            : throw new GitHubInvalidResponseException("GitHub returned a missing or negative commit count");
    }

    private static RepositoryNotFoundException RepositoryNotFound(CompareTarget target, Exception? innerException)
    {
        return new(
            message: $"the repository {target.Repository} was not found, or the caller cannot see it",
            innerException: innerException
        );
    }

    private static BranchNotFoundException BranchNotFound(
        CompareTarget target,
        string branch,
        Exception? innerException
    )
    {
        return new(
            message: $"the branch {branch} was not found in {target.Repository}",
            innerException: innerException
        );
    }

    [DebuggerDisplay("{Repository} {Base}...{Head}")]
    private readonly record struct CompareTarget(string Repository, string Base, string Head);
}
