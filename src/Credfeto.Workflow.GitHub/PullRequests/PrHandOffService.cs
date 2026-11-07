using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.BodyRules;
using Credfeto.Workflow.GitHub.Branches;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.Leaks;
using Credfeto.Workflow.GitHub.Validation;

namespace Credfeto.Workflow.GitHub.PullRequests;

public sealed partial class PrHandOffService : IPrHandOffService
{
    private const string APPROVAL_KEYWORD_RULE = "approval keyword";

    private const string CONVENTIONAL_COMMITS_RULE = "Conventional Commits title";

    private const string NOT_OPEN_RULE = "not open";

    private const string HEAD_CHANGED_RULE = "head changed";

    private const string OPEN_STATE = "open";

    private const string PULL_REQUEST_RULE = "pull_request";

    private const string REQUIRED_STATUS_CHECKS_RULE = "required_status_checks";

    // GitHub answers the rulesets endpoint with this 403 on a private repository whose plan has no rulesets
    private const string RULESETS_UNAVAILABLE_MESSAGE = "Upgrade to GitHub Pro or make this repository public";

    private const string REVIEW_RECORD_HEADING = "### AI Review Loop: reviewed ";

    private const string STEP_TITLE_AND_DESCRIPTION = "title and description set";

    private const string STEP_BOARD = "Workflow board set to Human Review";

    private const string STEP_READY = "marked ready for review";

    private const string STEP_REVIEW_RECORD = "review record posted";

    private const string STEP_AUTO_MERGE = "auto-merge turned on";

    private const string TITLE_SUBJECT = "title";

    private const string DESCRIPTION_SUBJECT = "description";

    private const string RECORD_SUBJECT = "record";

    private const string PULL_REQUEST_SUBJECT = "pull request";

    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    private static readonly IReadOnlyList<string> HoldLabels = [StandardLabels.BLOCKED, StandardLabels.ON_HOLD];

    private readonly IBoardWriter _boardWriter;
    private readonly IWorkflowBoardResolver _boardResolver;
    private readonly IGitHubGraphQlClient _graphQlClient;
    private readonly ILeakChecker _leakChecker;
    private readonly IPrChecksService _prChecks;
    private readonly IGitHubRestClient _restClient;
    private readonly IWorkflowStatusResolver _statusResolver;

    public PrHandOffService(
        IGitHubRestClient restClient,
        IGitHubGraphQlClient graphQlClient,
        ILeakChecker leakChecker,
        IWorkflowBoardResolver boardResolver,
        IWorkflowStatusResolver statusResolver,
        IBoardWriter boardWriter,
        IPrChecksService prChecks
    )
    {
        this._restClient = restClient;
        this._graphQlClient = graphQlClient;
        this._leakChecker = leakChecker;
        this._boardResolver = boardResolver;
        this._statusResolver = statusResolver;
        this._boardWriter = boardWriter;
        this._prChecks = prChecks;
    }

    public ValueTask<PrHandOffResult> HandOffAsync(
        string repository,
        int pullRequest,
        string title,
        string description,
        string? record,
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

        // GitHubNames.Repository guarantees exactly one separator between a non-empty owner and name
        int separator = validRepository.IndexOf('/', StringComparison.Ordinal);

        HandOffRequest request = new(
            Repository: validRepository,
            Owner: validRepository[..separator],
            Name: validRepository[(separator + 1)..],
            Number: pullRequest,
            Title: ValidTitle(title),
            Description: ValidText(value: description, parameterName: nameof(description)),
            Record: record is null ? null : ValidText(value: record, parameterName: nameof(record))
        );

        return this.HandOffValidAsync(request: request, cancellationToken: cancellationToken);
    }

    private static string ValidTitle(string title)
    {
        return TitleValidator.IsValidTitle(title)
            ? title
            : throw Usage(parameterName: nameof(title), message: TitleValidator.INVALID_TITLE_MESSAGE);
    }

    // Whitespace and invisible characters alone would show as an empty description or comment on GitHub
    private static string ValidText(string value, string parameterName)
    {
        return TitleValidator.HasVisibleText(value)
            ? value
            : throw Usage(parameterName: parameterName, message: $"the {parameterName} is empty");
    }

    // The message is fixed text naming the rule the value broke, never the rejected value itself
    private static ParameterValidationException Usage(string parameterName, string message)
    {
        return new(message) { ParameterName = parameterName, Rule = message };
    }

    private async ValueTask<PrHandOffResult> HandOffValidAsync(
        HandOffRequest request,
        CancellationToken cancellationToken
    )
    {
        PrHandOffResult? refused = await this.CheckTextAsync(request: request, cancellationToken: cancellationToken);

        if (refused is not null)
        {
            return refused;
        }

        // The board is resolved before anything is written, so a repository with no Workflow board fails here and
        // leaves the pull request untouched
        WorkflowProject project = await this._boardResolver.ResolveAsync(
            repository: request.Repository,
            cancellationToken: cancellationToken
        );

        ResolvedStatusOption option = await this._statusResolver.ResolveAsync(
            repository: request.Repository,
            project: project,
            state: WorkflowStates.HUMAN_REVIEW,
            cancellationToken: cancellationToken
        );

        PullRequestRead read = await this.ReadPullRequestAsync(request: request, cancellationToken: cancellationToken);

        if (read.Result is not null)
        {
            return read.Result;
        }

        PullRequestState state = read.State;

        PrHandOffResult? notReady =
            Unready(state)
            ?? await this.CheckRequiredChecksAsync(
                request: request,
                state: state,
                cancellationToken: cancellationToken
            );

        if (notReady is not null)
        {
            return notReady;
        }

        AutoMergePlan plan = await this.PlanAutoMergeAsync(
            request: request,
            state: state,
            cancellationToken: cancellationToken
        );

        return await this.WriteAsync(
            target: new(Request: request, State: state, Board: new(Project: project, Option: option), Plan: plan),
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<PrHandOffResult?> CheckTextAsync(
        HandOffRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!TitleValidator.IsValidPullRequestTitle(request.Title))
        {
            return Refused(subject: TITLE_SUBJECT, rule: CONVENTIONAL_COMMITS_RULE);
        }

        // A title gets the same rules as a body except its length, which the usage checks already limit
        LeakRefusal? titleLeak =
            this._leakChecker.CheckValue(subject: LeakSubject.TITLE, value: request.Title)
            ?? await this._leakChecker.CheckRepositoryReferencesAsync(
                repository: request.Repository,
                subject: LeakSubject.TITLE,
                text: request.Title,
                cancellationToken: cancellationToken
            );

        if (titleLeak is not null)
        {
            return Refused(subject: TITLE_SUBJECT, rule: LeakRuleNames.Name(titleLeak.Rule));
        }

        PrHandOffResult? description = await this.CheckBodyAsync(
            repository: request.Repository,
            subject: DESCRIPTION_SUBJECT,
            body: request.Description,
            cancellationToken: cancellationToken
        );

        if (description is not null || request.Record is null)
        {
            return description;
        }

        return await this.CheckBodyAsync(
            repository: request.Repository,
            subject: RECORD_SUBJECT,
            body: request.Record,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<PrHandOffResult?> CheckBodyAsync(
        string repository,
        string subject,
        string body,
        CancellationToken cancellationToken
    )
    {
        LeakRefusal? leak =
            this._leakChecker.CheckBody(body)
            ?? await this._leakChecker.CheckRepositoryReferencesAsync(
                repository: repository,
                subject: LeakSubject.BODY,
                text: body,
                cancellationToken: cancellationToken
            );

        if (leak is not null)
        {
            return Refused(subject: subject, rule: LeakRuleNames.Name(leak.Rule), lineNumber: leak.LineNumber);
        }

        // Such text could be read as a human approving the pull request; quoting a human's own words is allowed
        return ApprovalKeywordMatcher.FindFirstKeywordLine(body) is { } approval
            ? Refused(subject: subject, rule: APPROVAL_KEYWORD_RULE, lineNumber: approval.LineNumber)
            : null;
    }

    private async ValueTask<PullRequestRead> ReadPullRequestAsync(
        HandOffRequest request,
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
                    outcome: PrHandOffOutcome.NotFound,
                    message: $"the pull request {request.Reference} was not found, or the caller cannot see it"
                ),
                State: default
            );
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            state = null;
        }

        return state is { } found
            ? new(Result: null, State: found)
            : new(
                Result: NotHandedOff(request: request, reason: "could not read the pull request", steps: []),
                State: default
            );
    }

    private async ValueTask<PullRequestState?> ReadStateAsync(
        HandOffRequest request,
        CancellationToken cancellationToken
    )
    {
        PullRequestHandOffResponse response = await this._restClient.GetAsync(
            relativeUri: $"repos/{request.Repository}/pulls/{request.NumberText}",
            responseTypeInfo: GitHubJsonContext.Default.PullRequestHandOffResponse,
            cancellationToken: cancellationToken
        );

        if (
            response
            is not {
                NodeId: { Length: > 0 } nodeId,
                State: { } openState,
                Draft: { } draft,
                Head.Sha: { Length: > 0 } headSha,
                Base.Ref: { Length: > 0 } baseBranch,
            }
        )
        {
            return null;
        }

        return new(
            NodeId: nodeId,
            Open: StringComparer.Ordinal.Equals(x: openState, y: OPEN_STATE),
            Draft: draft,
            HeadSha: headSha,
            BaseBranch: baseBranch,
            Labels: [.. (response.Labels ?? []).Select(label => label?.Name).OfType<string>()]
        );
    }

    private static PrHandOffResult? Unready(PullRequestState state)
    {
        if (!state.Open)
        {
            return RefusedPr(NOT_OPEN_RULE);
        }

        // The standard spelling is reported rather than the label as it is spelt on the pull request
        string? held = HoldLabels.FirstOrDefault(hold =>
            state.Labels.Contains(value: hold, comparer: StringComparer.OrdinalIgnoreCase)
        );

        return held is null ? null : RefusedPr($"{held} label");
    }

    private async ValueTask<PrHandOffResult?> CheckRequiredChecksAsync(
        HandOffRequest request,
        PullRequestState state,
        CancellationToken cancellationToken
    )
    {
        PrChecksResponse checks = await this._prChecks.ReadAsync(
            repository: request.Repository,
            pullRequest: request.Number,
            sha: null,
            branch: null,
            all: false,
            cancellationToken: cancellationToken
        );

        // The checks are read for the head GitHub reports now, which a push may have moved since the first read
        if (!StringComparer.OrdinalIgnoreCase.Equals(x: checks.Sha, y: state.HeadSha))
        {
            return RefusedPr(HEAD_CHANGED_RULE);
        }

        return checks.Verdict switch
        {
            PrChecksVerdict.Passed => null,
            PrChecksVerdict.Failed => RefusedPr("required checks failed"),
            PrChecksVerdict.Pending => RefusedPr("required checks pending"),
            _ => RefusedPr("no checks"),
        };
    }

    private async ValueTask<AutoMergePlan> PlanAutoMergeAsync(
        HandOffRequest request,
        PullRequestState state,
        CancellationToken cancellationToken
    )
    {
        if (await this.AutoMergeDisallowedAsync(request: request, cancellationToken: cancellationToken))
        {
            return new(Kind: PrHandOffAutoMerge.Unavailable, Reason: "the repository does not allow auto-merge");
        }

        return await this.BaseNeedsNothingAsync(request: request, state: state, cancellationToken: cancellationToken)
            ? new(Kind: PrHandOffAutoMerge.NotArmed, Reason: "the base branch requires no check and no review")
            : new(Kind: PrHandOffAutoMerge.On, Reason: null);
    }

    // Only a setting GitHub reports as off counts: one that is missing or could not be read is tried
    private async ValueTask<bool> AutoMergeDisallowedAsync(HandOffRequest request, CancellationToken cancellationToken)
    {
        try
        {
            RepositoryAutoMergeResponse response = await this._restClient.GetAsync(
                relativeUri: $"repos/{request.Repository}",
                responseTypeInfo: GitHubJsonContext.Default.RepositoryAutoMergeResponse,
                cancellationToken: cancellationToken
            );

            return response.AllowAutoMerge is false;
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            return false;
        }
    }

    // GitHub refuses auto-merge on a pull request that can merge at once, so it is only skipped when the base is known
    // to need nothing: an unprotected branch whose rules were read and include neither a review nor a check
    private async ValueTask<bool> BaseNeedsNothingAsync(
        HandOffRequest request,
        PullRequestState state,
        CancellationToken cancellationToken
    )
    {
        try
        {
            string branchPath = GitHubPath.BranchPath(
                GitHubNames.Branch(value: state.BaseBranch, parameterName: "base")
            );

            BranchProtectedResponse branch = await this._restClient.GetAsync(
                relativeUri: $"repos/{request.Repository}/branches/{branchPath}",
                responseTypeInfo: GitHubJsonContext.Default.BranchProtectedResponse,
                cancellationToken: cancellationToken
            );

            if (branch.Protected is not false)
            {
                return false;
            }

            IReadOnlyList<BranchRuleResponse> rules = await this.ReadRulesAsync(
                repository: request.Repository,
                branchPath: branchPath,
                cancellationToken: cancellationToken
            );

            return !rules.Any(rule =>
                StringComparer.Ordinal.Equals(x: rule.Type, y: PULL_REQUEST_RULE)
                || StringComparer.Ordinal.Equals(x: rule.Type, y: REQUIRED_STATUS_CHECKS_RULE)
            );
        }
        catch (Exception exception) when (IsReadFailure(exception) || exception is ParameterValidationException)
        {
            return false;
        }
    }

    private async ValueTask<IReadOnlyList<BranchRuleResponse>> ReadRulesAsync(
        string repository,
        string branchPath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await this._restClient.GetAllPagesAsync(
                relativeUri: $"repos/{repository}/rules/branches/{branchPath}?per_page=100",
                pageTypeInfo: GitHubJsonContext.Default.BranchRuleResponseArray,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (IsRulesetsUnavailable(exception))
        {
            return [];
        }
    }

    private static bool IsRulesetsUnavailable(HttpRequestException exception)
    {
        return exception.StatusCode == HttpStatusCode.Forbidden
            && GitHubHttpFailure.UpstreamMessage(exception) is { } message
            && message.Contains(value: RULESETS_UNAVAILABLE_MESSAGE, comparisonType: StringComparison.Ordinal);
    }

    // The writes run in this order so a pull request is only marked ready once its title, description and board status
    // are final, and auto-merge is only turned on once the review record names the head that was checked
    private async ValueTask<PrHandOffResult> WriteAsync(HandOffTarget target, CancellationToken cancellationToken)
    {
        List<string> steps = [];

        WriteFailure? failure = await TryWriteAsync(
            write: () =>
                PullRequestWriter.UpdateTitleAndBodyAsync(
                    restClient: this._restClient,
                    repository: target.Request.Repository,
                    number: target.Request.Number,
                    title: target.Request.Title,
                    body: target.Request.Description,
                    cancellationToken: cancellationToken
                ),
            step: STEP_TITLE_AND_DESCRIPTION,
            steps: steps
        );

        failure ??= await TryWriteAsync(
            write: () => this.SetBoardAsync(target: target, cancellationToken: cancellationToken),
            step: STEP_BOARD,
            steps: steps
        );

        if (failure is null && target.State.Draft)
        {
            failure = await TryWriteAsync(
                write: () =>
                    PullRequestWriter.MarkReadyAsync(
                        graphQlClient: this._graphQlClient,
                        pullRequestNodeId: target.State.NodeId,
                        cancellationToken: cancellationToken
                    ),
                step: STEP_READY,
                steps: steps
            );
        }

        return failure is { } failed
            ? Failed(request: target.Request, failure: failed, steps: steps)
            : await this.RecordAndFinishAsync(target: target, steps: steps, cancellationToken: cancellationToken);
    }

    private async ValueTask<PrHandOffResult> RecordAndFinishAsync(
        HandOffTarget target,
        List<string> steps,
        CancellationToken cancellationToken
    )
    {
        Uri recordUrl;

        try
        {
            recordUrl = await this.PostRecordAsync(target: target, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (IsWriteFailure(exception))
        {
            return Failed(
                request: target.Request,
                failure: new(Step: STEP_REVIEW_RECORD, Exception: exception),
                steps: steps
            );
        }

        steps.Add(STEP_REVIEW_RECORD);

        return await this.FinishAsync(
            target: target,
            recordUrl: recordUrl,
            steps: steps,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<PrHandOffResult> FinishAsync(
        HandOffTarget target,
        Uri recordUrl,
        IReadOnlyList<string> steps,
        CancellationToken cancellationToken
    )
    {
        if (target.Plan.Kind != PrHandOffAutoMerge.On)
        {
            return HandedOff(target: target, recordUrl: recordUrl);
        }

        // A push since the checks were read would be merged without its checks having been judged, so auto-merge is
        // left off for a head that has moved, or that can no longer be read
        if (!await this.HeadUnchangedAsync(target: target, cancellationToken: cancellationToken))
        {
            return NotHandedOff(
                request: target.Request,
                reason: "the head changed or could not be read again, so auto-merge was not turned on",
                steps: steps
            );
        }

        WriteFailure? failure = await TryWriteAsync(
            write: () =>
                PullRequestWriter.EnableAutoMergeAsync(
                    graphQlClient: this._graphQlClient,
                    pullRequestNodeId: target.State.NodeId,
                    headSha: target.State.HeadSha,
                    cancellationToken: cancellationToken
                ),
            step: STEP_AUTO_MERGE,
            steps: []
        );

        return failure is { } failed
            ? Failed(request: target.Request, failure: failed, steps: steps)
            : HandedOff(target: target, recordUrl: recordUrl);
    }

    private async ValueTask<bool> HeadUnchangedAsync(HandOffTarget target, CancellationToken cancellationToken)
    {
        try
        {
            PullRequestState? again = await this.ReadStateAsync(
                request: target.Request,
                cancellationToken: cancellationToken
            );

            return again is { } state
                && StringComparer.OrdinalIgnoreCase.Equals(x: state.HeadSha, y: target.State.HeadSha);
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            return false;
        }
    }

    private static bool IsReadFailure(Exception exception)
    {
        return exception is GitHubInvalidResponseException or HttpRequestException;
    }

    private ValueTask SetBoardAsync(HandOffTarget target, CancellationToken cancellationToken)
    {
        return this._boardWriter.SetItemStatusAsync(
            owner: target.Request.Owner,
            repo: target.Request.Name,
            kind: WorkItemKind.PULL_REQUEST,
            number: target.Request.Number,
            project: target.Board.Project,
            fieldId: target.Board.Option.FieldId,
            fieldName: target.Board.Option.FieldName,
            optionId: target.Board.Option.OptionId,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<Uri> PostRecordAsync(HandOffTarget target, CancellationToken cancellationToken)
    {
        // The heading names the head that was checked, so the record says what was reviewed whatever is pushed later
        string heading = REVIEW_RECORD_HEADING + target.State.HeadSha;
        string body = target.Request.Record is null ? heading : $"{heading}\n\n{target.Request.Record}";

        IssueCommentResponse posted = await this._restClient.PostAsync(
            relativeUri: $"repos/{target.Request.Repository}/issues/{target.Request.NumberText}/comments",
            body: new IssueCommentCreateRequest(Body: body),
            requestTypeInfo: GitHubJsonContext.Default.IssueCommentCreateRequest,
            responseTypeInfo: GitHubJsonContext.Default.IssueCommentResponse,
            cancellationToken: cancellationToken
        );

        return
            CreatedItemUrl.TryParse(
                pattern: CommentUrl(),
                url: posted.HtmlUrl ?? string.Empty,
                out Uri? url,
                out int number
            )
            && number == target.Request.Number
            ? url
            : throw new GitHubInvalidResponseException("GitHub returned an unexpected URL for the review record");
    }

    private static async ValueTask<WriteFailure?> TryWriteAsync(Func<ValueTask> write, string step, List<string> steps)
    {
        try
        {
            await write();
        }
        catch (Exception exception) when (IsWriteFailure(exception))
        {
            return new(Step: step, Exception: exception);
        }

        steps.Add(step);

        return null;
    }

    private static bool IsWriteFailure(Exception exception)
    {
        return exception
            is BoardWriteException
                or GitHubGraphQlException
                or GitHubInvalidResponseException
                or HttpRequestException;
    }

    // A refused request stored nothing; any other failure came after the request was sent, and writes are never
    // retried, so GitHub may have made the change without the answer arriving
    private static bool MayHaveBeenWritten(Exception exception)
    {
        return exception switch
        {
            BoardWriteException { InnerException: { } inner } => MayHaveBeenWritten(inner),
            BoardWriteException => true,
            GitHubGraphQlException => false,
            HttpRequestException { StatusCode: { } statusCode } => !IsRejected(statusCode),
            _ => true,
        };
    }

    private static bool IsRejected(HttpStatusCode statusCode)
    {
        return statusCode
            is HttpStatusCode.BadRequest
                or HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden
                or HttpStatusCode.NotFound
                or HttpStatusCode.Gone
                or HttpStatusCode.UnprocessableEntity;
    }

    // Only the status is described, never the exception's message, so no upstream text reaches the caller
    private static string Describe(Exception exception)
    {
        return exception switch
        {
            BoardWriteException { InnerException: { } inner } => Describe(inner),
            BoardWriteException => "the Workflow board did not confirm the change",
            GitHubGraphQlException => "GitHub refused the change",
            HttpRequestException { StatusCode: { } statusCode } =>
                $"GitHub answered with status {((int)statusCode).ToString(CultureInfo.InvariantCulture)}",
            HttpRequestException => "the request to GitHub failed",
            _ => "the answer from GitHub could not be read",
        };
    }

    private static PrHandOffResult Failed(HandOffRequest request, WriteFailure failure, IReadOnlyList<string> steps)
    {
        string reason = Describe(failure.Exception);

        return MayHaveBeenWritten(failure.Exception)
            ? Result(
                outcome: PrHandOffOutcome.MayHaveBeenHandedOff,
                message: $"could not hand off {request.Reference}, whose last step may have been made ({reason}); read the pull request before trying again"
            ) with
            {
                StepsDone = [.. steps],
            }
            : NotHandedOff(request: request, reason: $"step failed: {failure.Step} ({reason})", steps: steps);
    }

    private static PrHandOffResult NotHandedOff(HandOffRequest request, string reason, IReadOnlyList<string> steps)
    {
        return Result(
            outcome: PrHandOffOutcome.NotHandedOff,
            message: $"{request.Reference} was not handed off: {reason}"
        ) with
        {
            StepsDone = [.. steps],
        };
    }

    private static PrHandOffResult HandedOff(HandOffTarget target, Uri recordUrl)
    {
        string autoMerge = target.Plan.Reason is { } reason ? $"auto-merge not turned on: {reason}" : "auto-merge on";

        return Result(
            outcome: PrHandOffOutcome.HandedOff,
            message: $"{target.Request.Reference} was handed off for human review; {autoMerge}"
        ) with
        {
            HeadSha = target.State.HeadSha,
            ReviewRecordUrl = recordUrl,
            AutoMerge = target.Plan.Kind,
            AutoMergeReason = target.Plan.Reason,
        };
    }

    private static PrHandOffResult RefusedPr(string rule)
    {
        return Refused(subject: PULL_REQUEST_SUBJECT, rule: rule);
    }

    // The refused text is never echoed back: only the subject, the rule and, for a body, the line are reported
    private static PrHandOffResult Refused(string subject, string rule, int? lineNumber = null)
    {
        string line = lineNumber is { } value
            ? $" (line {value.ToString(CultureInfo.InvariantCulture)})"
            : string.Empty;

        return Result(outcome: PrHandOffOutcome.Refused, message: $"{subject} refused: {rule}{line}") with
        {
            Rule = rule,
            LineNumber = lineNumber,
        };
    }

    private static PrHandOffResult Result(PrHandOffOutcome outcome, string message)
    {
        return new(
            Outcome: outcome,
            Message: message,
            HeadSha: null,
            ReviewRecordUrl: null,
            AutoMerge: null,
            AutoMergeReason: null,
            StepsDone: null,
            Rule: null,
            LineNumber: null
        );
    }

    // GitHub answers a comment on a pull request made through its issue number with the pull request URL
    [GeneratedRegex(
        pattern: @"^https://[A-Za-z0-9.-]+(:[0-9]+)?/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/(pull|issues)/(?<number>[0-9]+)#issuecomment-[0-9]+\z",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex CommentUrl();

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Repository}#{Number}: {Title}")]
    private readonly record struct HandOffRequest(
        string Repository,
        string Owner,
        string Name,
        int Number,
        string Title,
        string Description,
        string? Record
    )
    {
        public string NumberText => this.Number.ToString(CultureInfo.InvariantCulture);

        public string Reference => $"{this.Repository}#{this.NumberText}";
    }

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{HeadSha} into {BaseBranch}")]
    private readonly record struct PullRequestState(
        string NodeId,
        bool Open,
        bool Draft,
        string HeadSha,
        string BaseBranch,
        IReadOnlyList<string> Labels
    );

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Result}")]
    private readonly record struct PullRequestRead(PrHandOffResult? Result, PullRequestState State);

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Kind}: {Reason}")]
    private readonly record struct AutoMergePlan(PrHandOffAutoMerge Kind, string? Reason);

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Option.OptionId}")]
    private readonly record struct BoardTarget(WorkflowProject Project, ResolvedStatusOption Option);

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Request.Reference}")]
    private readonly record struct HandOffTarget(
        HandOffRequest Request,
        PullRequestState State,
        BoardTarget Board,
        AutoMergePlan Plan
    );

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Step}")]
    private readonly record struct WriteFailure(string Step, Exception Exception);
}
