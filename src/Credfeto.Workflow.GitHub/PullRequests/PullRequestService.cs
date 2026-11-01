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

namespace Credfeto.Workflow.GitHub.PullRequests;

public sealed class PullRequestService : IPullRequestService
{
    private const int MAX_PAGES = 100;

    private const string NOT_FOUND_ERROR = "Could not resolve to a PullRequest with the number";

    private readonly IGitHubGraphQlClient _graphQlClient;
    private readonly IGitHubRestClient _restClient;

    public PullRequestService(IGitHubGraphQlClient graphQlClient, IGitHubRestClient restClient)
    {
        this._graphQlClient = graphQlClient;
        this._restClient = restClient;
    }

    public async ValueTask<PullRequestView> ViewAsync(
        string repository,
        int pullRequest,
        IReadOnlyList<string>? fields,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (pullRequest <= 0)
        {
            throw ParameterValidationException.For(parameterName: nameof(pullRequest), rule: "must be above 0");
        }

        IReadOnlyList<string> requested = PullRequestViewFieldSelection.Resolve(
            fields: fields,
            parameterName: nameof(fields)
        );

        PullRequestViewRequest request = new(
            Repository: validRepository,
            PullRequest: pullRequest,
            Reference: $"{validRepository}#{pullRequest}",
            Fields: requested
        );

        try
        {
            return await this.ReadAsync(request: request, cancellationToken: cancellationToken);
        }
        catch (GitHubGraphQlException exception)
            when (exception.Errors.Any(error =>
                    error.Contains(value: NOT_FOUND_ERROR, comparisonType: StringComparison.Ordinal)
                )
            )
        {
            throw new PullRequestNotFoundException(
                message: $"pull request {request.Reference} was not found",
                innerException: exception
            );
        }
        catch (Exception exception)
            when (exception is GitHubGraphQlException or GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new PullRequestViewFailedException(
                message: PullRequestViewFailure.IncompleteMessage(request.Reference),
                innerException: exception
            );
        }
    }

    private async ValueTask<PullRequestView> ReadAsync(
        PullRequestViewRequest request,
        CancellationToken cancellationToken
    )
    {
        PullRequestViewNode node = await this.QueryAsync(
            request: request,
            query: PullRequestViewQuery.Build(request.Fields),
            after: null,
            cancellationToken: cancellationToken
        );

        PullRequestView view = new PullRequestViewProjector(reference: request.Reference, node: node).Project(
            request.Fields
        );

        if (request.Fields.Contains(PullRequestViewFields.CommitAuthors, StringComparer.Ordinal))
        {
            view = view with
            {
                CommitAuthors = await this.ReadCommitAuthorsAsync(
                    request: request,
                    first: node,
                    cancellationToken: cancellationToken
                ),
            };
        }

        if (request.Fields.Contains(PullRequestViewFields.Files, StringComparer.Ordinal))
        {
            view = view with
            {
                Files = await this.ReadFilesAsync(request: request, node: node, cancellationToken: cancellationToken),
            };
        }

        return view;
    }

    private async ValueTask<string> ReadCommitAuthorsAsync(
        PullRequestViewRequest request,
        PullRequestViewNode first,
        CancellationToken cancellationToken
    )
    {
        CommitAuthorCollector collector = new(request.Reference);
        string? cursor = collector.Accept(first.Commits);

        // The first page arrived with the main query, so the cap counts it too
        for (int page = 1; page < MAX_PAGES; page++)
        {
            if (cursor is null)
            {
                return collector.Result;
            }

            PullRequestViewNode next = await this.QueryAsync(
                request: request,
                query: PullRequestViewQuery.CommitsPage,
                after: cursor,
                cancellationToken: cancellationToken
            );

            cursor = collector.Accept(next.Commits);
        }

        return cursor is null ? collector.Result : throw PullRequestViewFailure.Incomplete(request.Reference);
    }

    private async ValueTask<IReadOnlyList<string>> ReadFilesAsync(
        PullRequestViewRequest request,
        PullRequestViewNode node,
        CancellationToken cancellationToken
    )
    {
        if (node.ChangedFiles is not int changed || changed < 0)
        {
            throw PullRequestViewFailure.Incomplete(request.Reference);
        }

        IReadOnlyList<PullRequestFileResponse?> files = await this._restClient.GetAllPagesAsync(
            relativeUri: $"repos/{request.Repository}/pulls/{request.PullRequest}/files?per_page=100",
            pageTypeInfo: GitHubJsonContext.Default.PullRequestFileResponseArray,
            cancellationToken: cancellationToken
        );

        if (files.Any(file => file?.FileName is null))
        {
            throw PullRequestViewFailure.Incomplete(request.Reference);
        }

        // GitHub stops listing files at its own limit, which would otherwise look like a complete list
        if (files.Count < changed)
        {
            throw PullRequestViewFailure.FilesIncomplete(
                reference: request.Reference,
                changed: changed,
                listed: files.Count
            );
        }

        return [.. files.Select(file => PullRequestViewText.Clean(file?.FileName))];
    }

    private async ValueTask<PullRequestViewNode> QueryAsync(
        PullRequestViewRequest request,
        string query,
        string? after,
        CancellationToken cancellationToken
    )
    {
        // GitHubNames.Repository guarantees exactly one separator between a non-empty owner and name
        int separator = request.Repository.IndexOf('/', StringComparison.Ordinal);

        PullRequestViewQueryData data = await this._graphQlClient.ExecuteAsync(
            query: query,
            variables: new PullRequestViewQueryVariables(
                Owner: request.Repository[..separator],
                Name: request.Repository[(separator + 1)..],
                Number: request.PullRequest,
                After: after
            ),
            variablesTypeInfo: GitHubJsonContext.Default.PullRequestViewQueryVariables,
            dataTypeInfo: GitHubJsonContext.Default.PullRequestViewQueryData,
            cancellationToken: cancellationToken
        );

        return data.Repository?.PullRequest ?? throw PullRequestViewFailure.Incomplete(request.Reference);
    }
}
