using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.RepoFiles;

public sealed class RepoFileService : IRepoFileService
{
    // The object media type answers a directory with an object holding its entries rather than a bare array, so every
    // kind of content reads as the same shape
    private const string OBJECT_MEDIA_TYPE = "application/vnd.github.object+json";

    private readonly IGitHubRestClient _restClient;

    public RepoFileService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public ValueTask<RepoFileResponse> ReadAsync(
        string repository,
        string path,
        string? gitRef,
        CancellationToken cancellationToken
    )
    {
        RepoFileRequest request = new(
            Repository: GitHubNames.Repository(value: repository, parameterName: nameof(repository)),
            Path: GitHubNames.RepositoryPath(value: path, parameterName: nameof(path)),
            GitRef: gitRef is null ? null : GitHubNames.GitRef(value: gitRef, parameterName: nameof(gitRef))
        );

        return this.ReadContentAsync(request: request, cancellationToken: cancellationToken);
    }

    private async ValueTask<RepoFileResponse> ReadContentAsync(
        RepoFileRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            RepoContentResponse response = await this._restClient.GetAsync(
                relativeUri: ContentUri(request),
                mediaType: OBJECT_MEDIA_TYPE,
                responseTypeInfo: GitHubJsonContext.Default.RepoContentResponse,
                cancellationToken: cancellationToken
            );

            return RepoFileProjector.Project(response: response, request: request);
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
        {
            // GitHub answers a ref with no commit with a 422 rather than a 404
            return NotFound(request.Repository);
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new RepoFileFailedException(
                message: $"could not read the content of {request.Repository}",
                innerException: exception
            );
        }
    }

    private static string ContentUri(RepoFileRequest request)
    {
        string uri = $"repos/{request.Repository}/contents/{GitHubPath.BranchPath(request.Path)}";

        return request.GitRef is null ? uri : $"{uri}?ref={Uri.EscapeDataString(request.GitRef)}";
    }

    // The message never names the path or the ref, so a caller cannot use it to probe what exists
    private static RepoFileResponse NotFound(string repository)
    {
        return new(
            Kind: RepoFileKind.NotFound,
            Message: $"the path or ref was not found in the repository {repository}, or the repository was not found, or the caller cannot see it",
            Repository: repository,
            Path: null,
            Ref: null,
            Size: null,
            Content: null,
            Entries: null
        );
    }
}
