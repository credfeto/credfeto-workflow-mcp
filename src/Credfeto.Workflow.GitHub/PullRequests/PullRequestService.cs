using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.BodyRules;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.PullRequests;

public sealed class PullRequestService : IPullRequestService
{
    private const int MAX_PAGES = 100;

    private const string NOT_FOUND_ERROR = "Could not resolve to a PullRequest with the number";

    private const string STATE_OPEN = "open";

    private const string STATE_CLOSED = "closed";

    private const string MERGED_RULE = "a merged pull request cannot be closed or reopened";

    private readonly IGitHubGraphQlClient _graphQlClient;
    private readonly ILeakChecker _leakChecker;
    private readonly IGitHubRestClient _restClient;

    public PullRequestService(
        IGitHubGraphQlClient graphQlClient,
        IGitHubRestClient restClient,
        ILeakChecker leakChecker
    )
    {
        this._graphQlClient = graphQlClient;
        this._restClient = restClient;
        this._leakChecker = leakChecker;
    }

    public ValueTask<PullRequestStateChangeResponse> CloseAsync(
        string repository,
        int pullRequest,
        string? comment,
        CancellationToken cancellationToken
    )
    {
        return this.ChangeStateAsync(
            repository: repository,
            pullRequest: pullRequest,
            comment: comment,
            change: PullRequestStateChange.Close,
            cancellationToken: cancellationToken
        );
    }

    public ValueTask<PullRequestStateChangeResponse> ReopenAsync(
        string repository,
        int pullRequest,
        string? comment,
        CancellationToken cancellationToken
    )
    {
        return this.ChangeStateAsync(
            repository: repository,
            pullRequest: pullRequest,
            comment: comment,
            change: PullRequestStateChange.Reopen,
            cancellationToken: cancellationToken
        );
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

        PullRequestViewRequest request = new(Repository: validRepository, PullRequest: pullRequest, Fields: requested);

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

        // Files is only ever requested on its own, so at most one of these follow-up reads runs
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
        int pages = 1;

        while (cursor is not null)
        {
            if (pages == MAX_PAGES)
            {
                throw PullRequestViewFailure.Incomplete(request.Reference);
            }

            PullRequestViewNode next = await this.QueryAsync(
                request: request,
                query: PullRequestViewQuery.CommitsPage,
                after: cursor,
                cancellationToken: cancellationToken
            );

            cursor = collector.Accept(next.Commits);
            pages++;
        }

        return collector.Result;
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

    private async ValueTask<PullRequestStateChangeResponse> ChangeStateAsync(
        string repository,
        int pullRequest,
        string? comment,
        PullRequestStateChange change,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (pullRequest <= 0)
        {
            throw ParameterValidationException.For(parameterName: "pr", rule: "must be above 0");
        }

        string? validComment = string.IsNullOrWhiteSpace(comment) ? null : this.CheckStateChangeComment(comment);
        string reference = $"{validRepository}#{pullRequest}";
        string number = pullRequest.ToString(CultureInfo.InvariantCulture);
        string pullUri = $"repos/{validRepository}/pulls/{number}";

        PullRequestCurrentState current = await this.ReadForStateChangeAsync(
            pullUri: pullUri,
            reference: reference,
            pullRequest: pullRequest,
            change: change,
            cancellationToken: cancellationToken
        );

        // A merged pull request is also closed, so the merge is checked first or a close would report already-closed
        if (current.Merged)
        {
            throw new PullRequestStateChangeRefusedException(
                $"{change.Verb} of pull request {reference} was refused: {MERGED_RULE}"
            )
            {
                Rule = MERGED_RULE,
            };
        }

        if (StringComparer.Ordinal.Equals(x: current.State, y: change.Target))
        {
            return new(PullRequest: reference, Outcome: change.AlreadyOutcome, Url: current.Url);
        }

        return await this.WriteStateChangeAsync(
            commentUri: $"repos/{validRepository}/issues/{number}/comments",
            pullUri: pullUri,
            reference: reference,
            pullRequest: pullRequest,
            comment: validComment,
            change: change,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<PullRequestCurrentState> ReadForStateChangeAsync(
        string pullUri,
        string reference,
        int pullRequest,
        PullRequestStateChange change,
        CancellationToken cancellationToken
    )
    {
        try
        {
            PullRequestStateResponse response = await this._restClient.GetAsync(
                relativeUri: pullUri,
                responseTypeInfo: GitHubJsonContext.Default.PullRequestStateResponse,
                cancellationToken: cancellationToken
            );

            return CheckedState(response: response, pullRequest: pullRequest);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // An issue shares the pull request number space, and GitHub answers 404 for one asked for as a pull request
            throw new PullRequestNotFoundException(
                message: $"pull request {reference} was not found",
                innerException: exception
            );
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            throw StateChangeFailed(reference: reference, change: change, commentPosted: false, exception: exception);
        }
    }

    private async ValueTask<PullRequestStateChangeResponse> WriteStateChangeAsync(
        string commentUri,
        string pullUri,
        string reference,
        int pullRequest,
        string? comment,
        PullRequestStateChange change,
        CancellationToken cancellationToken
    )
    {
        if (comment is not null)
        {
            try
            {
                await this._restClient.PostAsync(
                    relativeUri: commentUri,
                    body: new IssueCommentCreateRequest(Body: comment),
                    requestTypeInfo: GitHubJsonContext.Default.IssueCommentCreateRequest,
                    responseTypeInfo: GitHubJsonContext.Default.IssueCommentResponse,
                    cancellationToken: cancellationToken
                );
            }
            catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
            {
                throw StateChangeFailed(
                    reference: reference,
                    change: change,
                    commentPosted: false,
                    exception: exception
                );
            }
        }

        try
        {
            PullRequestStateResponse response = await this._restClient.PatchAsync(
                relativeUri: pullUri,
                body: new PullRequestStateRequest(State: change.Target),
                requestTypeInfo: GitHubJsonContext.Default.PullRequestStateRequest,
                responseTypeInfo: GitHubJsonContext.Default.PullRequestStateResponse,
                cancellationToken: cancellationToken
            );

            PullRequestCurrentState changed = CheckedState(response: response, pullRequest: pullRequest);

            return StringComparer.Ordinal.Equals(x: changed.State, y: change.Target)
                ? new(PullRequest: reference, Outcome: change.Outcome, Url: changed.Url)
                : throw new GitHubInvalidResponseException("GitHub did not confirm the pull request state change");
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            throw StateChangeFailed(
                reference: reference,
                change: change,
                commentPosted: comment is not null,
                exception: exception
            );
        }
    }

    private static PullRequestCurrentState CheckedState(PullRequestStateResponse response, int pullRequest)
    {
        if (response.Number != pullRequest)
        {
            throw new GitHubInvalidResponseException("GitHub returned another pull request");
        }

        if (response.State is not (STATE_OPEN or STATE_CLOSED))
        {
            throw new GitHubInvalidResponseException("GitHub returned a pull request with an unknown state");
        }

        if (response.Merged is not bool merged)
        {
            throw new GitHubInvalidResponseException("GitHub returned a pull request without its merged flag");
        }

        if (
            response.HtmlUrl is null
            || !Uri.TryCreate(uriString: response.HtmlUrl, uriKind: UriKind.Absolute, out Uri? url)
            || !UriSchemes.IsHttp(url)
        )
        {
            throw new GitHubInvalidResponseException("GitHub returned a pull request without a valid link");
        }

        return new(State: response.State, Merged: merged, Url: url);
    }

    private static PullRequestStateChangeFailedException StateChangeFailed(
        string reference,
        PullRequestStateChange change,
        bool commentPosted,
        Exception exception
    )
    {
        // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message, which
        // says when the comment landed so the caller does not post it a second time on a retry
        return new(
            message: commentPosted
                ? $"could not {change.Verb} pull request {reference}; the comment was posted"
                : $"could not {change.Verb} pull request {reference}",
            innerException: exception
        );
    }

    private string CheckStateChangeComment(string comment)
    {
        return StateChangeCommentRules.Check(leakChecker: this._leakChecker, comment: comment) is { } refusal
            ? throw new PullRequestStateChangeRefusedException(StateChangeCommentRules.Message(refusal))
            {
                Rule = refusal.Rule,
                LineNumber = refusal.LineNumber,
            }
            : comment;
    }
}
