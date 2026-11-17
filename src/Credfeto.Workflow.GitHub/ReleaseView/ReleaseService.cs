using System;
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

namespace Credfeto.Workflow.GitHub.ReleaseView;

public sealed class ReleaseService : IReleaseService
{
    private readonly IGitHubRestClient _restClient;

    public ReleaseService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
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
