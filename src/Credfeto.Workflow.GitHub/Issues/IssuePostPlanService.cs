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
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.Leaks;
using Credfeto.Workflow.GitHub.Validation;

namespace Credfeto.Workflow.GitHub.Issues;

public sealed class IssuePostPlanService : IIssuePostPlanService
{
    private const string APPROVAL_KEYWORD_RULE = "approval keyword";

    private const string EMPTY_BODY = "the body is empty";

    private const string NO_BOARD = "no board";

    private const string ON_HOLD_HYPHENATED = "On-Hold";

    private readonly ICommentAddService _commentAddService;

    private readonly ILabelService _labelService;

    private readonly ILeakChecker _leakChecker;

    private readonly IGitHubRestClient _restClient;

    private readonly IWorkflowStatusSetService _workflowStatusSetService;

    public IssuePostPlanService(
        IGitHubRestClient restClient,
        ILeakChecker leakChecker,
        ICommentAddService commentAddService,
        ILabelService labelService,
        IWorkflowStatusSetService workflowStatusSetService
    )
    {
        this._restClient = restClient;
        this._leakChecker = leakChecker;
        this._commentAddService = commentAddService;
        this._labelService = labelService;
        this._workflowStatusSetService = workflowStatusSetService;
    }

    public ValueTask<IssuePostPlanResponse> PostAsync(
        string repository,
        int issue,
        string body,
        CancellationToken cancellationToken
    )
    {
        // Every argument is validated before the first GitHub call, so invalid input never costs one
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (issue <= 0)
        {
            throw ParameterValidationException.For(parameterName: nameof(issue), rule: "must be above 0");
        }

        if (!TitleValidator.HasVisibleText(body))
        {
            throw new ParameterValidationException(EMPTY_BODY) { ParameterName = nameof(body), Rule = EMPTY_BODY };
        }

        return this.CheckAndPostAsync(
            repository: validRepository,
            issue: issue,
            body: body,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<IssuePostPlanResponse> CheckAndPostAsync(
        string repository,
        int issue,
        string body,
        CancellationToken cancellationToken
    )
    {
        await this.CheckBodyAsync(repository: repository, body: body, cancellationToken: cancellationToken);

        if (!PlanHeading.IsPresent(body))
        {
            throw Refused(rule: $"no '{PlanHeading.TEXT}' line");
        }

        await this.CheckIssueAsync(repository: repository, issue: issue, cancellationToken: cancellationToken);

        Uri commentUrl = await this.PostCommentAsync(
            repository: repository,
            issue: issue,
            body: body,
            cancellationToken: cancellationToken
        );

        IReadOnlyList<string> labels = await this.AddBlockedAsync(
            repository: repository,
            issue: issue,
            commentUrl: commentUrl,
            cancellationToken: cancellationToken
        );

        string board = await this.SetPlanningAsync(
            repository: repository,
            issue: issue,
            commentUrl: commentUrl,
            cancellationToken: cancellationToken
        );

        return new(CommentUrl: commentUrl, Labels: labels, Board: board);
    }

    // The same checks as comment add, run here first so a refused body is reported before the issue is read
    private async ValueTask CheckBodyAsync(string repository, string body, CancellationToken cancellationToken)
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
            throw BodyRefused(rule: LeakRuleNames.Name(leak.Rule), lineNumber: leak.LineNumber);
        }

        // Such a comment could be read as a human approving the plan it posts
        if (ApprovalKeywordMatcher.FindFirstKeywordLine(body) is { } approval)
        {
            throw BodyRefused(rule: APPROVAL_KEYWORD_RULE, lineNumber: approval.LineNumber);
        }
    }

    private async ValueTask CheckIssueAsync(string repository, int issue, CancellationToken cancellationToken)
    {
        string number = issue.ToString(CultureInfo.InvariantCulture);
        string reference = $"{repository}#{number}";
        bool open;
        bool onHold;

        try
        {
            IssueResponse response = await this._restClient.GetAsync(
                relativeUri: $"repos/{repository}/issues/{number}",
                responseTypeInfo: GitHubJsonContext.Default.IssueResponse,
                cancellationToken: cancellationToken
            );

            // GitHub serves pull requests from the issues endpoint too, so one is reported as no such issue
            if (IssueKind.IsPullRequest(issue: issue, response: response))
            {
                throw new IssueNotFoundException($"issue {reference} was not found");
            }

            open = IsOpen(response.State);
            onHold = IsOnHold(response.Labels);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new IssueNotFoundException(message: $"issue {reference} was not found", innerException: exception);
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new IssueViewFailedException(message: $"could not read issue {reference}", innerException: exception);
        }

        if (!open)
        {
            throw Refused(rule: "the issue is not open");
        }

        if (onHold)
        {
            throw Refused(rule: "On Hold label");
        }
    }

    private static bool IsOpen(string? state)
    {
        return state switch
        {
            "open" => true,
            "closed" => false,
            _ => throw new GitHubInvalidResponseException("GitHub returned an issue with an unknown state"),
        };
    }

    private static bool IsOnHold(IReadOnlyList<IssueLabelResponse?>? labels)
    {
        return (labels ?? []).Any(label =>
            IsOnHoldName(
                label?.Name ?? throw new GitHubInvalidResponseException("GitHub returned a label without a name")
            )
        );
    }

    private static bool IsOnHoldName(string name)
    {
        return StringComparer.OrdinalIgnoreCase.Equals(x: name, y: StandardLabels.ON_HOLD)
            || StringComparer.OrdinalIgnoreCase.Equals(x: name, y: ON_HOLD_HYPHENATED);
    }

    // The comment goes first, so Blocked and Planning are never set without a plan; nothing else is written when it
    // was not posted, including when it may have been
    private async ValueTask<Uri> PostCommentAsync(
        string repository,
        int issue,
        string body,
        CancellationToken cancellationToken
    )
    {
        CommentAddResult result = await this._commentAddService.AddAsync(
            repository: repository,
            pullRequest: null,
            issue: issue,
            body: body,
            cancellationToken: cancellationToken
        );

        return result is { Outcome: CommentAddOutcome.Posted, Url: { } url }
            ? url
            : throw new IssuePostPlanFailedException(result.Message);
    }

    private async ValueTask<IReadOnlyList<string>> AddBlockedAsync(
        string repository,
        int issue,
        Uri commentUrl,
        CancellationToken cancellationToken
    )
    {
        try
        {
            LabelChangeResponse response = await this._labelService.AddAsync(
                repository: repository,
                pullRequest: null,
                issue: issue,
                labels: [StandardLabels.BLOCKED],
                cancellationToken: cancellationToken
            );

            return response.Labels;
        }
        catch (Exception exception) when (IsPartWayFailure(exception))
        {
            throw PartWay(
                step: "could not add the Blocked label",
                commentUrl: commentUrl,
                stepsDone: [CommentPosted(commentUrl)],
                exception: exception
            );
        }
    }

    // A repository without a Workflow board is not a failure: the plan is still posted and the issue Blocked
    private async ValueTask<string> SetPlanningAsync(
        string repository,
        int issue,
        Uri commentUrl,
        CancellationToken cancellationToken
    )
    {
        try
        {
            WorkflowStatusSetResponse response = await this._workflowStatusSetService.SetAsync(
                repository: repository,
                pullRequest: null,
                issue: issue,
                status: WorkflowStates.PLANNING,
                cancellationToken: cancellationToken
            );

            return response.Status;
        }
        catch (WorkflowBoardNotFoundException)
        {
            return NO_BOARD;
        }
        catch (Exception exception) when (IsPartWayFailure(exception))
        {
            throw PartWay(
                step: $"could not set the Workflow status to {WorkflowStates.PLANNING}",
                commentUrl: commentUrl,
                stepsDone: [CommentPosted(commentUrl), $"{StandardLabels.BLOCKED} added"],
                exception: exception
            );
        }
    }

    // Once the comment exists every failure is reported with the steps done, so its URL is never lost; a cancellation
    // is left to propagate as the caller asked for it
    private static bool IsPartWayFailure(Exception exception)
    {
        return exception is not OperationCanceledException;
    }

    private static string CommentPosted(Uri commentUrl)
    {
        return $"comment posted {commentUrl}";
    }

    private static IssuePostPlanFailedException PartWay(
        string step,
        Uri commentUrl,
        IReadOnlyList<string> stepsDone,
        Exception exception
    )
    {
        // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
        return new(
            message: $"{step}; steps done: {string.Join(separator: ", ", values: stepsDone)}",
            innerException: exception
        )
        {
            CommentUrl = commentUrl,
            StepsDone = stepsDone,
        };
    }

    private static IssuePostPlanRefusedException Refused(string rule)
    {
        return new($"post-plan refused: {rule}") { Rule = rule };
    }

    // The body is never echoed back: only the rule and, for a rule that applies to one line, the line are reported
    private static IssuePostPlanRefusedException BodyRefused(string rule, int? lineNumber)
    {
        string line = lineNumber is { } value
            ? $" (line {value.ToString(CultureInfo.InvariantCulture)})"
            : string.Empty;

        return new($"body refused: {rule}{line}") { Rule = rule, LineNumber = lineNumber };
    }
}
