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
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.Leaks;
using Credfeto.Workflow.GitHub.Validation;

namespace Credfeto.Workflow.GitHub.Blocks;

public sealed class BlockService : IBlockService
{
    private const string APPROVAL_KEYWORD_RULE = "approval keyword";

    private const string EMPTY_BODY = "the body is empty";

    private const string ISSUE_KIND = "issue";

    private const string PULL_REQUEST_KIND = "pull request";

    private readonly ICommentAddService _commentAddService;

    private readonly ILabelService _labelService;

    private readonly ILeakChecker _leakChecker;

    private readonly IGitHubRestClient _restClient;

    public BlockService(
        IGitHubRestClient restClient,
        ILeakChecker leakChecker,
        ICommentAddService commentAddService,
        ILabelService labelService
    )
    {
        this._restClient = restClient;
        this._leakChecker = leakChecker;
        this._commentAddService = commentAddService;
        this._labelService = labelService;
    }

    public ValueTask<BlockResult> BlockIssueAsync(
        string repository,
        int issue,
        string body,
        CancellationToken cancellationToken
    )
    {
        string validRepository = Validate(
            repository: repository,
            number: issue,
            parameterName: nameof(issue),
            body: body
        );

        return this.BlockAsync(
            repository: validRepository,
            number: issue,
            isPullRequest: false,
            body: body,
            cancellationToken: cancellationToken
        );
    }

    public ValueTask<BlockResult> BlockPullRequestAsync(
        string repository,
        int pullRequest,
        string body,
        CancellationToken cancellationToken
    )
    {
        string validRepository = Validate(repository: repository, number: pullRequest, parameterName: "pr", body: body);

        return this.BlockAsync(
            repository: validRepository,
            number: pullRequest,
            isPullRequest: true,
            body: body,
            cancellationToken: cancellationToken
        );
    }

    // Every argument is validated before the first GitHub call, so invalid input never costs one
    private static string Validate(string repository, int number, string parameterName, string body)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (number <= 0)
        {
            throw ParameterValidationException.For(parameterName: parameterName, rule: "must be above 0");
        }

        if (!TitleValidator.HasVisibleText(body))
        {
            throw new ParameterValidationException(EMPTY_BODY) { ParameterName = nameof(body), Rule = EMPTY_BODY };
        }

        return validRepository;
    }

    private async ValueTask<BlockResult> BlockAsync(
        string repository,
        int number,
        bool isPullRequest,
        string body,
        CancellationToken cancellationToken
    )
    {
        BlockResult? refusal = await this.CheckBodyAsync(
            repository: repository,
            isPullRequest: isPullRequest,
            body: body,
            cancellationToken: cancellationToken
        );

        if (refusal is not null)
        {
            return refusal;
        }

        string reference = $"{repository}#{number.ToString(CultureInfo.InvariantCulture)}";
        (BlockResult? readFailure, bool alreadyBlocked) = await this.ReadAsync(
            repository: repository,
            number: number,
            isPullRequest: isPullRequest,
            reference: reference,
            cancellationToken: cancellationToken
        );

        if (readFailure is not null)
        {
            return readFailure;
        }

        CommentAddResult comment = await this._commentAddService.AddAsync(
            repository: repository,
            pullRequest: isPullRequest ? number : null,
            issue: isPullRequest ? null : number,
            body: body,
            cancellationToken: cancellationToken
        );

        // The comment goes first so Blocked is never added without the question; nothing else is written when it was
        // not posted, including when it may have been
        return comment is { Outcome: CommentAddOutcome.Posted, Url: { } commentUrl }
            ? await this.AddBlockedAsync(
                repository: repository,
                number: number,
                isPullRequest: isPullRequest,
                reference: reference,
                commentUrl: commentUrl,
                alreadyBlocked: alreadyBlocked,
                cancellationToken: cancellationToken
            )
            : CommentNotPosted(comment);
    }

    // The same checks as comment add, then the env-block marker rule, run first so a refused body is reported before
    // anything is read or written
    private async ValueTask<BlockResult?> CheckBodyAsync(
        string repository,
        bool isPullRequest,
        string body,
        CancellationToken cancellationToken
    )
    {
        LeakRefusal? leak = this._leakChecker.CheckBody(body)
            ?? await this._leakChecker.CheckRepositoryReferencesAsync(
                repository: repository,
                subject: LeakSubject.BODY,
                text: body,
                cancellationToken: cancellationToken
            );

        if (leak is not null)
        {
            return Refused(rule: LeakRuleNames.Name(leak.Rule), lineNumber: leak.LineNumber);
        }

        // Such a comment could be read as a human approving a plan
        if (ApprovalKeywordMatcher.FindFirstKeywordLine(body) is { } approval)
        {
            return Refused(rule: APPROVAL_KEYWORD_RULE, lineNumber: approval.LineNumber);
        }

        // An env-block marker tells the orchestrator a pull request is waiting on a container image, so it means
        // nothing on an issue, and on a pull request only the exact form is read
        return isPullRequest ? MalformedMarker(body) : MarkerOnAnIssue(body);
    }

    private static BlockResult? MalformedMarker(string body)
    {
        return EnvBlockMarker.FindMalformedMarkerLine(body) is { } line
            ? Refused(rule: EnvBlockMarker.MALFORMED_RULE, lineNumber: line)
            : null;
    }

    private static BlockResult? MarkerOnAnIssue(string body)
    {
        return EnvBlockMarker.FindMarkerLine(body) is { } line
            ? Refused(rule: EnvBlockMarker.ON_AN_ISSUE_RULE, lineNumber: line)
            : null;
    }

    private async ValueTask<(BlockResult? Failure, bool AlreadyBlocked)> ReadAsync(
        string repository,
        int number,
        bool isPullRequest,
        string reference,
        CancellationToken cancellationToken
    )
    {
        string kind = Kind(isPullRequest);

        try
        {
            IssueResponse response = await this._restClient.GetAsync(
                relativeUri: $"repos/{repository}/issues/{number.ToString(CultureInfo.InvariantCulture)}",
                responseTypeInfo: GitHubJsonContext.Default.IssueResponse,
                cancellationToken: cancellationToken
            );

            // GitHub serves pull requests from the issues endpoint too, so the wrong kind is reported as not found
            return IssueKind.IsPullRequest(issue: number, response: response) == isPullRequest
                ? (null, IsBlocked(response.Labels))
                : (NotFound(kind: kind, reference: reference), false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return (NotFound(kind: kind, reference: reference), false);
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // Nothing has been sent that could create a comment, so whatever went wrong it was not posted
            return (
                Failed(
                    outcome: BlockOutcome.NotPosted,
                    message: $"could not read {kind} {reference}, so the comment was not posted: {GitHubHttpFailure.Describe(exception)}"
                ),
                false
            );
        }
    }

    private static bool IsBlocked(IReadOnlyList<IssueLabelResponse?>? labels)
    {
        return (labels ?? []).Any(static label =>
            label?.Name is { } name && StringComparer.OrdinalIgnoreCase.Equals(x: name, y: StandardLabels.BLOCKED)
        );
    }

    private async ValueTask<BlockResult> AddBlockedAsync(
        string repository,
        int number,
        bool isPullRequest,
        string reference,
        Uri commentUrl,
        bool alreadyBlocked,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // Adding a label it already has leaves it unchanged, so an item already Blocked still gets the call
            _ = await this._labelService.AddAsync(
                repository: repository,
                pullRequest: isPullRequest ? number : null,
                issue: isPullRequest ? null : number,
                labels: [StandardLabels.BLOCKED],
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Once the comment exists every failure names it, so its URL is never lost
            return new(
                Outcome: BlockOutcome.LabelNotAdded,
                Message: $"could not add the {StandardLabels.BLOCKED} label to {reference}; steps done: comment posted {commentUrl}",
                CommentUrl: commentUrl,
                AlreadyBlocked: alreadyBlocked,
                Rule: null,
                LineNumber: null
            );
        }

        string message = alreadyBlocked
            ? $"the comment was posted on {reference}, which was already {StandardLabels.BLOCKED}"
            : $"the comment was posted on {reference} and {StandardLabels.BLOCKED} was added";

        return new(
            Outcome: BlockOutcome.Blocked,
            Message: message,
            CommentUrl: commentUrl,
            AlreadyBlocked: alreadyBlocked,
            Rule: null,
            LineNumber: null
        );
    }

    private static BlockResult CommentNotPosted(CommentAddResult comment)
    {
        return comment.Outcome switch
        {
            CommentAddOutcome.Refused => new(
                Outcome: BlockOutcome.Refused,
                Message: comment.Message,
                CommentUrl: null,
                AlreadyBlocked: false,
                Rule: comment.Rule,
                LineNumber: comment.LineNumber
            ),
            CommentAddOutcome.NotPosted => Failed(outcome: BlockOutcome.NotPosted, message: comment.Message),
            _ => Failed(outcome: BlockOutcome.MayHaveBeenPosted, message: comment.Message),
        };
    }

    private static string Kind(bool isPullRequest)
    {
        return isPullRequest ? PULL_REQUEST_KIND : ISSUE_KIND;
    }

    private static BlockResult NotFound(string kind, string reference)
    {
        return Failed(outcome: BlockOutcome.NotFound, message: $"{kind} {reference} was not found");
    }

    // The body is never echoed back: only the rule and, for a rule that applies to one line, the line are reported
    private static BlockResult Refused(string rule, int? lineNumber)
    {
        string line = lineNumber is { } value
            ? $" (line {value.ToString(CultureInfo.InvariantCulture)})"
            : string.Empty;

        return new(
            Outcome: BlockOutcome.Refused,
            Message: $"body refused: {rule}{line}",
            CommentUrl: null,
            AlreadyBlocked: false,
            Rule: rule,
            LineNumber: lineNumber
        );
    }

    private static BlockResult Failed(BlockOutcome outcome, string message)
    {
        return new(
            Outcome: outcome,
            Message: message,
            CommentUrl: null,
            AlreadyBlocked: false,
            Rule: null,
            LineNumber: null
        );
    }
}
