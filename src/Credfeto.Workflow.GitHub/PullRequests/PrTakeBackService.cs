using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.BodyRules;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.Leaks;
using Credfeto.Workflow.GitHub.Validation;

namespace Credfeto.Workflow.GitHub.PullRequests;

public sealed class PrTakeBackService : IPrTakeBackService
{
    private const string APPROVAL_KEYWORD_RULE = "approval keyword";

    private const string NOT_OPEN_RULE = "not open";

    private const string OPEN_STATE = "open";

    private const string EMPTY_BODY = "the body is empty";

    private const string BODY_SUBJECT = "body";

    private const string PULL_REQUEST_SUBJECT = "pull request";

    private const string STEP_AUTO_MERGE = "auto-merge off";

    private const string STEP_DRAFT = "draft";

    private const string STEP_BOARD = "Workflow board set to Development";

    private const string STEP_COMMENT = "comment posted";

    private readonly IBoardWriter _boardWriter;
    private readonly IWorkflowBoardResolver _boardResolver;
    private readonly IGitHubGraphQlClient _graphQlClient;
    private readonly ILeakChecker _leakChecker;
    private readonly IGitHubRestClient _restClient;
    private readonly IWorkflowStatusResolver _statusResolver;

    public PrTakeBackService(
        IGitHubRestClient restClient,
        IGitHubGraphQlClient graphQlClient,
        ILeakChecker leakChecker,
        IWorkflowBoardResolver boardResolver,
        IWorkflowStatusResolver statusResolver,
        IBoardWriter boardWriter
    )
    {
        this._restClient = restClient;
        this._graphQlClient = graphQlClient;
        this._leakChecker = leakChecker;
        this._boardResolver = boardResolver;
        this._statusResolver = statusResolver;
        this._boardWriter = boardWriter;
    }

    public ValueTask<PrTakeBackResult> TakeBackAsync(
        string repository,
        int pullRequest,
        string body,
        CancellationToken cancellationToken
    )
    {
        // Every argument is validated before the first GitHub call, so invalid input never costs one and is reported
        // as such rather than as a GitHub failure
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (pullRequest <= 0)
        {
            throw ParameterValidationException.For(parameterName: "pr", rule: "must be above 0");
        }

        // Whitespace and invisible characters alone would show as an empty comment on GitHub
        if (!TitleValidator.HasVisibleText(body))
        {
            throw new ParameterValidationException(EMPTY_BODY) { ParameterName = nameof(body), Rule = EMPTY_BODY };
        }

        // GitHubNames.Repository guarantees exactly one separator between a non-empty owner and name
        int separator = validRepository.IndexOf('/', StringComparison.Ordinal);

        TakeBackRequest request = new(
            Repository: validRepository,
            Owner: validRepository[..separator],
            Name: validRepository[(separator + 1)..],
            Number: pullRequest,
            Body: body
        );

        return this.TakeBackValidAsync(request: request, cancellationToken: cancellationToken);
    }

    private async ValueTask<PrTakeBackResult> TakeBackValidAsync(
        TakeBackRequest request,
        CancellationToken cancellationToken
    )
    {
        PrTakeBackResult? refused = await this.CheckBodyAsync(request: request, cancellationToken: cancellationToken);

        if (refused is not null)
        {
            return refused;
        }

        PullRequestBoardTarget board = await PullRequestBoardTarget.ResolveAsync(
            boardResolver: this._boardResolver,
            statusResolver: this._statusResolver,
            repository: request.Repository,
            state: WorkflowStates.DEVELOPMENT,
            cancellationToken: cancellationToken
        );

        PullRequestRead read = await this.ReadPullRequestAsync(request: request, cancellationToken: cancellationToken);

        return read.Result
            ?? (
                read.State.Open
                    ? await this.WriteAsync(
                        target: new(Request: request, State: read.State, Board: board),
                        cancellationToken: cancellationToken
                    )
                    : Refused(subject: PULL_REQUEST_SUBJECT, rule: NOT_OPEN_RULE)
            );
    }

    private async ValueTask<PrTakeBackResult?> CheckBodyAsync(
        TakeBackRequest request,
        CancellationToken cancellationToken
    )
    {
        LeakRefusal? leak = this._leakChecker.CheckBody(request.Body)
            ?? await this._leakChecker.CheckRepositoryReferencesAsync(
                repository: request.Repository,
                subject: LeakSubject.BODY,
                text: request.Body,
                cancellationToken: cancellationToken
            );

        if (leak is not null)
        {
            return Refused(subject: BODY_SUBJECT, rule: LeakRuleNames.Name(leak.Rule), lineNumber: leak.LineNumber);
        }

        // Such a comment could be read as a human approving a plan; quoting a human's own words is allowed
        return ApprovalKeywordMatcher.FindFirstKeywordLine(request.Body) is { } approval
            ? Refused(subject: BODY_SUBJECT, rule: APPROVAL_KEYWORD_RULE, lineNumber: approval.LineNumber)
            : null;
    }

    private async ValueTask<PullRequestRead> ReadPullRequestAsync(
        TakeBackRequest request,
        CancellationToken cancellationToken
    )
    {
        PullRequestState? state;

        try
        {
            state = await this.ReadStateAsync(request: request, cancellationToken: cancellationToken);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // An issue shares the pull request number space, and GitHub answers 404 for one asked for as a pull request
            return new(
                Result: Result(
                    outcome: PrTakeBackOutcome.NotFound,
                    message: $"the pull request {request.Reference} was not found, or the caller cannot see it"
                ),
                State: default
            );
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            state = null;
        }

        return state is { } found
            ? new(Result: null, State: found)
            : new(
                Result: NotTakenBack(request: request, reason: "could not read the pull request", steps: []),
                State: default
            );
    }

    private async ValueTask<PullRequestState?> ReadStateAsync(
        TakeBackRequest request,
        CancellationToken cancellationToken
    )
    {
        PullRequestTakeBackResponse response = await this._restClient.GetAsync(
            relativeUri: $"repos/{request.Repository}/pulls/{request.NumberText}",
            responseTypeInfo: GitHubJsonContext.Default.PullRequestTakeBackResponse,
            cancellationToken: cancellationToken
        );

        // A missing or unexpected auto_merge is never taken as off, since a draft with auto-merge still on can be merged
        if (
            response
            is not {
                NodeId: { Length: > 0 } nodeId,
                State: { } openState,
                Draft: { } draft,
                AutoMerge.ValueKind: JsonValueKind.Null or JsonValueKind.Object,
            }
        )
        {
            return null;
        }

        return new(
            NodeId: nodeId,
            Open: StringComparer.Ordinal.Equals(x: openState, y: OPEN_STATE),
            Draft: draft,
            AutoMerge: response.AutoMerge.ValueKind == JsonValueKind.Object
        );
    }

    // Auto-merge is turned off first because a draft with auto-merge still on can be merged, so a failure there stops
    // before anything else; a step whose state already holds is not made again but is still reported as done
    private async ValueTask<PrTakeBackResult> WriteAsync(TakeBackTarget target, CancellationToken cancellationToken)
    {
        List<string> steps = [];

        PullRequestWriteFailure? failure = target.State.AutoMerge
            ? await PullRequestWriteSteps.TryWriteAsync(
                write: () =>
                    PullRequestWriter.DisableAutoMergeAsync(
                        graphQlClient: this._graphQlClient,
                        pullRequestNodeId: target.State.NodeId,
                        cancellationToken: cancellationToken
                    ),
                step: STEP_AUTO_MERGE,
                steps: steps
            )
            : Skipped(step: STEP_AUTO_MERGE, steps: steps);

        failure ??= target.State.Draft
            ? Skipped(step: STEP_DRAFT, steps: steps)
            : await PullRequestWriteSteps.TryWriteAsync(
                write: () =>
                    PullRequestWriter.ConvertToDraftAsync(
                        graphQlClient: this._graphQlClient,
                        pullRequestNodeId: target.State.NodeId,
                        cancellationToken: cancellationToken
                    ),
                step: STEP_DRAFT,
                steps: steps
            );

        failure ??= await PullRequestWriteSteps.TryWriteAsync(
            write: () =>
                target.Board.SetAsync(
                    boardWriter: this._boardWriter,
                    owner: target.Request.Owner,
                    name: target.Request.Name,
                    number: target.Request.Number,
                    cancellationToken: cancellationToken
                ),
            step: STEP_BOARD,
            steps: steps
        );

        return failure is { } failed
            ? Failed(request: target.Request, failure: failed, steps: steps)
            : await this.CommentAndFinishAsync(target: target, steps: steps, cancellationToken: cancellationToken);
    }

    private static PullRequestWriteFailure? Skipped(string step, List<string> steps)
    {
        steps.Add(step);

        return null;
    }

    private async ValueTask<PrTakeBackResult> CommentAndFinishAsync(
        TakeBackTarget target,
        List<string> steps,
        CancellationToken cancellationToken
    )
    {
        Uri commentUrl;

        try
        {
            commentUrl = await PullRequestWriter.PostCommentAsync(
                restClient: this._restClient,
                repository: target.Request.Repository,
                number: target.Request.Number,
                body: target.Request.Body,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception) when (PullRequestWriteSteps.IsWriteFailure(exception))
        {
            return Failed(
                request: target.Request,
                failure: new(Step: STEP_COMMENT, Exception: exception),
                steps: steps
            );
        }

        steps.Add(STEP_COMMENT);

        return Result(
            outcome: PrTakeBackOutcome.TakenBack,
            message: $"{target.Request.Reference} was taken back: auto-merge off, draft, {WorkflowStates.DEVELOPMENT}"
        ) with
        {
            CommentUrl = commentUrl,
            AutoMergeWasOn = target.State.AutoMerge,
            AlreadyDraft = target.State.Draft,
            StepsDone = [.. steps],
        };
    }

    private static PrTakeBackResult Failed(
        TakeBackRequest request,
        PullRequestWriteFailure failure,
        IReadOnlyList<string> steps
    )
    {
        string reason = PullRequestWriteSteps.Describe(failure.Exception);

        return PullRequestWriteSteps.MayHaveBeenWritten(failure.Exception)
            ? Result(
                outcome: PrTakeBackOutcome.MayHaveBeenTakenBack,
                message: $"could not take back {request.Reference}, whose last step may have been made ({reason}); read the pull request before trying again"
            ) with
            {
                StepsDone = [.. steps],
            }
            : NotTakenBack(request: request, reason: $"step failed: {failure.Step} ({reason})", steps: steps);
    }

    private static PrTakeBackResult NotTakenBack(TakeBackRequest request, string reason, IReadOnlyList<string> steps)
    {
        return Result(
            outcome: PrTakeBackOutcome.NotTakenBack,
            message: $"{request.Reference} was not taken back: {reason}"
        ) with
        {
            StepsDone = [.. steps],
        };
    }

    // The refused text is never echoed back: only the subject, the rule and, for the body, the line are reported
    private static PrTakeBackResult Refused(string subject, string rule, int? lineNumber = null)
    {
        string line = lineNumber is { } value
            ? $" (line {value.ToString(CultureInfo.InvariantCulture)})"
            : string.Empty;

        return Result(outcome: PrTakeBackOutcome.Refused, message: $"{subject} refused: {rule}{line}") with
        {
            Rule = rule,
            LineNumber = lineNumber,
        };
    }

    private static PrTakeBackResult Result(PrTakeBackOutcome outcome, string message)
    {
        return new(
            Outcome: outcome,
            Message: message,
            CommentUrl: null,
            AutoMergeWasOn: null,
            AlreadyDraft: null,
            StepsDone: null,
            Rule: null,
            LineNumber: null
        );
    }

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Repository}#{Number}")]
    private readonly record struct TakeBackRequest(
        string Repository,
        string Owner,
        string Name,
        int Number,
        string Body
    )
    {
        public string NumberText => this.Number.ToString(CultureInfo.InvariantCulture);

        public string Reference => $"{this.Repository}#{this.NumberText}";
    }

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{NodeId}: draft {Draft}, auto-merge {AutoMerge}")]
    private readonly record struct PullRequestState(string NodeId, bool Open, bool Draft, bool AutoMerge);

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Result}")]
    private readonly record struct PullRequestRead(PrTakeBackResult? Result, PullRequestState State);

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Request.Reference}")]
    private readonly record struct TakeBackTarget(
        TakeBackRequest Request,
        PullRequestState State,
        PullRequestBoardTarget Board
    );
}
