using System;
using System.Buffers;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Tags;

public sealed class TagViewService : ITagViewService
{
    private const string TAG_REF_PREFIX = "refs/tags/";
    private const string COMMIT_TYPE = "commit";
    private const string TAG_TYPE = "tag";
    private const int SHA_LENGTH = 40;

    // GitHub always names an object by its lower-case SHA, so anything else is not a SHA GitHub would return
    private static readonly SearchValues<char> LowerHexDigits = SearchValues.Create("0123456789abcdef");

    private readonly IGitHubRestClient _restClient;

    public TagViewService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    // The arguments are validated before anything is read, so an invalid one is reported as such rather than as a
    // failed read
    public ValueTask<TagViewResponse> ViewAsync(string repository, string tag, CancellationToken cancellationToken)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        string validTag = GitHubNames.Tag(value: tag, parameterName: nameof(tag));

        return this.ViewOrFailAsync(repository: validRepository, tag: validTag, cancellationToken: cancellationToken);
    }

    private async ValueTask<TagViewResponse> ViewOrFailAsync(
        string repository,
        string tag,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await this.ResolveAsync(repository: repository, tag: tag, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new TagViewFailedException(
                message: $"could not read the tag {tag} in {repository}",
                innerException: exception
            );
        }
    }

    private async ValueTask<TagViewResponse> ResolveAsync(
        string repository,
        string tag,
        CancellationToken cancellationToken
    )
    {
        GitTarget target = await this.ReadTagRefAsync(
            repository: repository,
            tag: tag,
            cancellationToken: cancellationToken
        );

        if (!StringComparer.Ordinal.Equals(x: target.Type, y: TAG_TYPE))
        {
            return PointsToCommit(target: target, type: TagType.Lightweight, repository: repository, tag: tag);
        }

        // Only this one step is followed, so a tag of a tag is refused rather than chased
        GitTarget tagged = await this.ReadTagObjectAsync(
            repository: repository,
            sha: target.Sha,
            cancellationToken: cancellationToken
        );

        return PointsToCommit(target: tagged, type: TagType.Annotated, repository: repository, tag: tag);
    }

    private async ValueTask<GitTarget> ReadTagRefAsync(
        string repository,
        string tag,
        CancellationToken cancellationToken
    )
    {
        GitRefResponse response;

        try
        {
            response = await this._restClient.GetAsync(
                relativeUri: $"repos/{repository}/git/ref/tags/{GitHubPath.BranchPath(tag)}",
                responseTypeInfo: GitHubJsonContext.Default.GitRefResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw await this.MissingTagOrRepositoryAsync(
                repository: repository,
                tag: tag,
                tagNotFound: exception,
                cancellationToken: cancellationToken
            );
        }

        // An exact match is required, so neither a branch of the same name nor a longer tag that starts with it is taken
        return StringComparer.Ordinal.Equals(x: response.Ref, y: TAG_REF_PREFIX + tag)
            ? ValidTarget(response.Object)
            : throw new GitHubInvalidResponseException("GitHub returned a ref other than the tag asked for");
    }

    // The tag object was just named by GitHub, so a 404 here is an unexpected result rather than a missing tag
    private async ValueTask<GitTarget> ReadTagObjectAsync(
        string repository,
        string sha,
        CancellationToken cancellationToken
    )
    {
        GitTagResponse response = await this._restClient.GetAsync(
            relativeUri: $"repos/{repository}/git/tags/{sha}",
            responseTypeInfo: GitHubJsonContext.Default.GitTagResponse,
            cancellationToken: cancellationToken
        );

        return ValidTarget(response.Object);
    }

    // A missing tag and a missing or hidden repository both answer 404, so the repository is read to tell them apart
    private async ValueTask<Exception> MissingTagOrRepositoryAsync(
        string repository,
        string tag,
        HttpRequestException tagNotFound,
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

        return new TagNotFoundException(
            message: $"the tag {tag} was not found in {repository}",
            innerException: tagNotFound
        );
    }

    private static GitTarget ValidTarget(GitObjectResponse? target)
    {
        return target is { Type: { Length: > 0 } type, Sha: { } sha } && IsSha(sha)
            ? new(Type: type, Sha: sha)
            : throw new GitHubInvalidResponseException("GitHub returned a git object without a type or a valid SHA");
    }

    private static bool IsSha(string value)
    {
        return value.Length == SHA_LENGTH && !value.AsSpan().ContainsAnyExcept(LowerHexDigits);
    }

    private static TagViewResponse PointsToCommit(GitTarget target, TagType type, string repository, string tag)
    {
        return StringComparer.Ordinal.Equals(x: target.Type, y: COMMIT_TYPE)
            ? new(Sha: target.Sha, Type: type)
            : throw new TagViewFailedException(
                $"the tag {tag} in {repository} does not point to a commit, itself or through one annotated tag"
            );
    }
}
