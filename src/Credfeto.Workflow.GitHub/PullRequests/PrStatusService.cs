using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Issues;

namespace Credfeto.Workflow.GitHub.PullRequests;

public sealed class PrStatusService : IPrStatusService
{
    // Only the fields the status needs: the default set would also page through every commit author
    private static readonly IReadOnlyList<string> ViewFields =
    [
        PullRequestViewFields.State,
        PullRequestViewFields.Draft,
        PullRequestViewFields.Title,
        PullRequestViewFields.Url,
        PullRequestViewFields.Base,
        PullRequestViewFields.Head,
        PullRequestViewFields.HeadSha,
        PullRequestViewFields.AutoMerge,
        PullRequestViewFields.ReviewDecision,
        PullRequestViewFields.MergeState,
        PullRequestViewFields.Mergeable,
        PullRequestViewFields.Labels,
        PullRequestViewFields.Assignees,
        PullRequestViewFields.Author,
        PullRequestViewFields.ChangedFiles,
    ];

    private readonly IPrChecksService _checksService;
    private readonly IPullRequestCommentsReader _commentsReader;
    private readonly IPrReadyAtService _readyAtService;
    private readonly IWorkflowStatusCheckService _statusCheckService;
    private readonly IPullRequestService _viewService;

    public PrStatusService(
        IPullRequestService viewService,
        IPrChecksService checksService,
        IWorkflowStatusCheckService statusCheckService,
        IPullRequestCommentsReader commentsReader,
        IPrReadyAtService readyAtService
    )
    {
        this._viewService = viewService;
        this._checksService = checksService;
        this._statusCheckService = statusCheckService;
        this._commentsReader = commentsReader;
        this._readyAtService = readyAtService;
    }

    public async ValueTask<PrStatusResponse> ReadAsync(
        string repository,
        int pullRequest,
        CancellationToken cancellationToken
    )
    {
        // The view is read first: it validates the input, and a pull request that does not exist is reported as such
        // rather than as a failure of whichever later read happened to notice
        PullRequestView view = await this._viewService.ViewAsync(
            repository: repository,
            pullRequest: pullRequest,
            fields: ViewFields,
            cancellationToken: cancellationToken
        );

        PrChecksResponse checks = await this._checksService.ReadAsync(
            repository: repository,
            pullRequest: pullRequest,
            sha: null,
            branch: null,
            all: false,
            cancellationToken: cancellationToken
        );

        string headSha = Required(value: view.HeadSha, reference: view.Pr);

        if (!StringComparer.Ordinal.Equals(x: headSha, y: checks.Sha))
        {
            throw new PrStatusHeadChangedException(
                $"the head of pull request {view.Pr} changed while its status was being read"
            );
        }

        string? board = await this.ReadBoardAsync(
            repository: repository,
            pullRequest: pullRequest,
            cancellationToken: cancellationToken
        );

        PullRequestComments comments = await this._commentsReader.ReadAsync(
            repository: repository,
            pullRequest: pullRequest,
            cancellationToken: cancellationToken
        );

        PrReadyAtResponse readyAt = await this._readyAtService.ReadAsync(
            repository: repository,
            pullRequest: pullRequest,
            cancellationToken: cancellationToken
        );

        return Build(view: view, checks: checks, board: board, comments: comments, readyAt: readyAt);
    }

    private async ValueTask<string?> ReadBoardAsync(
        string repository,
        int pullRequest,
        CancellationToken cancellationToken
    )
    {
        try
        {
            WorkflowStatusCheckResult result = await this._statusCheckService.CheckAsync(
                repository: repository,
                pullRequest: pullRequest,
                issue: null,
                cancellationToken: cancellationToken
            );

            return result.Outcome == WorkflowStatusCheckOutcome.Found ? result.State : null;
        }
        catch (WorkflowBoardNotFoundException)
        {
            // A repository without a board is normal; any other board failure still fails the whole status
            return null;
        }
    }

    private static PrStatusResponse Build(
        PullRequestView view,
        PrChecksResponse checks,
        string? board,
        PullRequestComments comments,
        PrReadyAtResponse readyAt
    )
    {
        string reference = view.Pr;

        return new(
            Pr: reference,
            State: Required(value: view.State, reference: reference),
            Draft: Required(value: view.Draft, reference: reference),
            Title: Required(value: view.Title, reference: reference),
            Url: Required(value: view.Url, reference: reference),
            Base: Required(value: view.Base, reference: reference),
            Head: Required(value: view.Head, reference: reference),
            HeadSha: checks.Sha,
            AutoMerge: Required(value: view.AutoMerge, reference: reference),
            ReviewDecision: Required(value: view.ReviewDecision, reference: reference),
            MergeState: Required(value: view.MergeState, reference: reference),
            Mergeable: Required(value: view.Mergeable, reference: reference),
            Labels: Required(value: view.Labels, reference: reference),
            Assignees: Required(value: view.Assignees, reference: reference),
            Author: Required(value: view.Author, reference: reference),
            ChangedFiles: view.ChangedFiles ?? throw PullRequestViewFailure.Incomplete(reference),
            Board: board,
            ReadyAt: readyAt.ReadyAt,
            ReadySource: readyAt.Source,
            Checks: new(
                Verdict: checks.Verdict,
                Failing: Names(checks: checks, verdict: PrChecksVerdict.Failed),
                Pending: Names(checks: checks, verdict: PrChecksVerdict.Pending)
            ),
            Activity: PrStatusActivityCounter.Count(
                comments: comments,
                readyAt: ReadyTime(readyAt: readyAt, reference: reference)
            )
        );
    }

    private static IReadOnlyList<string> Names(PrChecksResponse checks, PrChecksVerdict verdict)
    {
        return [.. checks.Checks.Where(check => check.Verdict == verdict).Select(check => check.Name)];
    }

    private static DateTime ReadyTime(PrReadyAtResponse readyAt, string reference)
    {
        return IssueCommentsProjector.TryParseTimestamp(value: readyAt.ReadyAt, out DateTime time)
            ? time
            : throw new PrReadyAtFailedException(
                $"could not read when pull request {reference} became ready for review"
            );
    }

    private static T Required<T>(T? value, string reference)
        where T : class
    {
        return value ?? throw PullRequestViewFailure.Incomplete(reference);
    }
}
