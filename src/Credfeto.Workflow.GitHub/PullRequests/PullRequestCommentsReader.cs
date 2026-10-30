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

public sealed class PullRequestCommentsReader : IPullRequestCommentsReader
{
    // GitHub returns at most 100 nodes per page, so this allows 10,000 entries in each list before giving up
    private const int MAX_PAGES = 100;

    private const string NOT_FOUND_ERROR = "Could not resolve to a PullRequest with the number";

    private const string COMMENTS_QUERY =
        "query($o:String!,$r:String!,$n:Int!,$comments:Boolean!,$reviews:Boolean!,$threads:Boolean!,$commentsAfter:String,$reviewsAfter:String,$threadsAfter:String){repository(owner:$o,name:$r){pullRequest(number:$n){reviewDecision comments(first:100,after:$commentsAfter) @include(if:$comments){totalCount pageInfo{hasNextPage endCursor} nodes{fullDatabaseId author{__typename login} authorAssociation createdAt isMinimized url body}} reviews(first:100,after:$reviewsAfter) @include(if:$reviews){totalCount pageInfo{hasNextPage endCursor} nodes{fullDatabaseId author{__typename login} authorAssociation state submittedAt url body}} reviewThreads(first:100,after:$threadsAfter) @include(if:$threads){totalCount pageInfo{hasNextPage endCursor} nodes{isResolved isOutdated path line originalLine comments(first:100){totalCount pageInfo{hasNextPage endCursor} nodes{fullDatabaseId author{__typename login} authorAssociation createdAt isMinimized url body replyTo{fullDatabaseId} pullRequestReview{fullDatabaseId}}}}}}}}";

    private readonly IGitHubGraphQlClient _graphQlClient;

    public PullRequestCommentsReader(IGitHubGraphQlClient graphQlClient)
    {
        this._graphQlClient = graphQlClient;
    }

    public async ValueTask<PullRequestComments> ReadAsync(
        string repository,
        int pullRequest,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (pullRequest <= 0)
        {
            throw ParameterValidationException.For(parameterName: nameof(pullRequest), rule: "must be above 0");
        }

        string reference = $"{validRepository}#{pullRequest}";

        try
        {
            return await this.ReadAllPagesAsync(
                repository: validRepository,
                pullRequest: pullRequest,
                reference: reference,
                cancellationToken: cancellationToken
            );
        }
        catch (GitHubGraphQlException exception)
            when (exception.Errors.Any(error =>
                    error.Contains(value: NOT_FOUND_ERROR, comparisonType: StringComparison.Ordinal)
                )
            )
        {
            throw new PullRequestNotFoundException(
                message: $"pull request {reference} was not found",
                innerException: exception
            );
        }
        catch (Exception exception)
            when (exception is GitHubGraphQlException or GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new PullRequestCommentsIncompleteException(
                message: PullRequestCommentsFailure.IncompleteMessage(reference),
                innerException: exception
            );
        }
    }

    private async ValueTask<PullRequestComments> ReadAllPagesAsync(
        string repository,
        int pullRequest,
        string reference,
        CancellationToken cancellationToken
    )
    {
        // GitHubNames.Repository guarantees exactly one separator between a non-empty owner and name
        int separator = repository.IndexOf('/', StringComparison.Ordinal);
        string owner = repository[..separator];
        string name = repository[(separator + 1)..];

        PullRequestCommentsProjector projector = new(reference);
        PagedList<IssueCommentNode, PullRequestComment> comments = new(
            project: projector.Comment,
            reference: reference
        );
        PagedList<ReviewNode, PullRequestReview> reviews = new(project: projector.Review, reference: reference);
        PagedList<ReviewThreadNode, PullRequestThread> threads = new(project: projector.Thread, reference: reference);

        for (int page = 0; page < MAX_PAGES; ++page)
        {
            PullRequestCommentsQueryData data = await this._graphQlClient.ExecuteAsync(
                query: COMMENTS_QUERY,
                variables: new PullRequestCommentsQueryVariables(
                    Owner: owner,
                    Name: name,
                    Number: pullRequest,
                    Comments: comments.Pending,
                    Reviews: reviews.Pending,
                    Threads: threads.Pending,
                    CommentsAfter: comments.After,
                    ReviewsAfter: reviews.After,
                    ThreadsAfter: threads.After
                ),
                variablesTypeInfo: GitHubJsonContext.Default.PullRequestCommentsQueryVariables,
                dataTypeInfo: GitHubJsonContext.Default.PullRequestCommentsQueryData,
                cancellationToken: cancellationToken
            );

            PullRequestNode node =
                data.Repository?.PullRequest ?? throw PullRequestCommentsFailure.Incomplete(reference);

            AcceptPage(node: node, comments: comments, reviews: reviews, threads: threads);

            if (!comments.Pending && !reviews.Pending && !threads.Pending)
            {
                return BuildResult(
                    reference: reference,
                    reviewDecision: node.ReviewDecision,
                    comments: comments.Items,
                    reviews: reviews.Items,
                    threads: threads.Items
                );
            }
        }

        throw PullRequestCommentsFailure.Incomplete(reference);
    }

    private static void AcceptPage(
        PullRequestNode node,
        PagedList<IssueCommentNode, PullRequestComment> comments,
        PagedList<ReviewNode, PullRequestReview> reviews,
        PagedList<ReviewThreadNode, PullRequestThread> threads
    )
    {
        if (comments.Pending)
        {
            comments.Accept(node.Comments);
        }

        if (reviews.Pending)
        {
            reviews.Accept(node.Reviews);
        }

        if (threads.Pending)
        {
            threads.Accept(node.ReviewThreads);
        }
    }

    private static PullRequestComments BuildResult(
        string reference,
        string? reviewDecision,
        IReadOnlyList<PullRequestComment> comments,
        IReadOnlyList<PullRequestReview> reviews,
        IReadOnlyList<PullRequestThread> threads
    )
    {
        IReadOnlyList<PullRequestReview> withText =
        [
            .. reviews.Where(review => !PullRequestCommentsProjector.IsEmptyWrapper(review)),
        ];

        return new(
            Pr: reference,
            ReviewDecision: reviewDecision ?? string.Empty,
            Comments: comments,
            Reviews: withText,
            Threads: threads,
            Summary: new(
                Comments: comments.Count,
                Reviews: withText.Count,
                ReviewsEmpty: reviews.Count - withText.Count,
                Threads: threads.Count,
                Unresolved: threads.Count(thread => !thread.Resolved),
                Inline: threads.Sum(thread => thread.CommentCount)
            )
        );
    }
}
