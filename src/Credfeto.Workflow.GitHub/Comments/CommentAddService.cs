using System;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
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

namespace Credfeto.Workflow.GitHub.Comments;

public sealed partial class CommentAddService : ICommentAddService
{
    private const string APPROVAL_KEYWORD_RULE = "approval keyword";

    private const string EMPTY_BODY = "the body is empty";

    private const string PR_PARAMETER = "pr";

    private const string ISSUE_PARAMETER = "issue";

    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    private readonly ILeakChecker _leakChecker;

    private readonly IGitHubRestClient _restClient;

    public CommentAddService(IGitHubRestClient restClient, ILeakChecker leakChecker)
    {
        this._restClient = restClient;
        this._leakChecker = leakChecker;
    }

    public ValueTask<CommentAddResult> AddAsync(
        string repository,
        int? pullRequest,
        int? issue,
        string body,
        CancellationToken cancellationToken
    )
    {
        // Every argument is validated before the first GitHub call, so invalid input never costs one and is reported
        // as such rather than as a GitHub failure
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        int number = TargetNumber(pullRequest: pullRequest, issue: issue);

        // Whitespace and invisible characters alone would show as an empty comment on GitHub
        if (!TitleValidator.HasVisibleText(body))
        {
            throw new ParameterValidationException(EMPTY_BODY) { ParameterName = nameof(body), Rule = EMPTY_BODY };
        }

        return this.CheckAndPostAsync(
            validRepository: validRepository,
            number: number,
            body: body,
            cancellationToken: cancellationToken
        );
    }

    // A pull request's conversation comments are issue comments, so either names the same number space
    private static int TargetNumber(int? pullRequest, int? issue)
    {
        if (pullRequest.HasValue == issue.HasValue)
        {
            throw ParameterValidationException.For(
                parameterName: PR_PARAMETER,
                rule: "exactly one of pr or issue must be given"
            );
        }

        (int number, string parameterName) = pullRequest is { } pullRequestNumber
            ? (pullRequestNumber, PR_PARAMETER)
            : (issue.GetValueOrDefault(), ISSUE_PARAMETER);

        return number > 0
            ? number
            : throw ParameterValidationException.For(parameterName: parameterName, rule: "must be above 0");
    }

    private async ValueTask<CommentAddResult> CheckAndPostAsync(
        string validRepository,
        int number,
        string body,
        CancellationToken cancellationToken
    )
    {
        LeakRefusal? leak =
            this._leakChecker.CheckBody(body)
            ?? await this._leakChecker.CheckRepositoryReferencesAsync(
                repository: validRepository,
                subject: LeakSubject.BODY,
                text: body,
                cancellationToken: cancellationToken
            );

        if (leak is not null)
        {
            return Refused(rule: LeakRuleNames.Name(leak.Rule), lineNumber: leak.LineNumber);
        }

        // Such a comment could be read as a human approving a plan; quoting a human's own words is allowed
        if (ApprovalKeywordMatcher.FindFirstKeywordLine(body) is { } approval)
        {
            return Refused(rule: APPROVAL_KEYWORD_RULE, lineNumber: approval.LineNumber);
        }

        return await this.PostAsync(
            validRepository: validRepository,
            number: number,
            body: body,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask<CommentAddResult> PostAsync(
        string validRepository,
        int number,
        string body,
        CancellationToken cancellationToken
    )
    {
        string issueNumber = number.ToString(CultureInfo.InvariantCulture);
        string reference = $"{validRepository}#{issueNumber}";
        IssueCommentResponse posted;

        try
        {
            posted = await this._restClient.PostAsync(
                relativeUri: $"repos/{validRepository}/issues/{issueNumber}/comments",
                body: new IssueCommentCreateRequest(Body: body),
                requestTypeInfo: GitHubJsonContext.Default.IssueCommentCreateRequest,
                responseTypeInfo: GitHubJsonContext.Default.IssueCommentResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (GitHubHttpFailure.IsRejected(exception.StatusCode))
        {
            return Failed(
                outcome: CommentAddOutcome.NotPosted,
                message: $"could not post the comment on {reference}: {GitHubHttpFailure.Describe(exception)}"
            );
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The request was sent and never retried, so GitHub may have stored the comment without the answer arriving
            return Failed(
                outcome: CommentAddOutcome.MayHaveBeenPosted,
                message: $"could not post the comment on {reference}, which may have been posted ({GitHubHttpFailure.Describe(exception)}); read the comments before posting again"
            );
        }

        // GitHub answers a comment on a pull request made through its issue number with the pull request URL, so
        // either form is accepted whichever was asked for
        if (
            !CreatedItemUrl.TryParse(
                pattern: CommentUrl(),
                url: posted.HtmlUrl ?? string.Empty,
                out Uri? url,
                out int urlNumber
            )
            || urlNumber != number
        )
        {
            return Failed(
                outcome: CommentAddOutcome.MayHaveBeenPosted,
                message: $"unexpected result for the comment, which may have been posted on {reference}"
            );
        }

        return new(
            Outcome: CommentAddOutcome.Posted,
            Message: $"the comment was posted on {reference}",
            Url: url,
            Rule: null,
            LineNumber: null
        );
    }

    // The body is never echoed back: only the rule and, for a rule that applies to one line, the line are reported
    private static CommentAddResult Refused(string rule, int? lineNumber)
    {
        string line = lineNumber is { } value
            ? $" (line {value.ToString(CultureInfo.InvariantCulture)})"
            : string.Empty;

        return new(
            Outcome: CommentAddOutcome.Refused,
            Message: $"body refused: {rule}{line}",
            Url: null,
            Rule: rule,
            LineNumber: lineNumber
        );
    }

    private static CommentAddResult Failed(CommentAddOutcome outcome, string message)
    {
        return new(Outcome: outcome, Message: message, Url: null, Rule: null, LineNumber: null);
    }

    [GeneratedRegex(
        pattern: @"^https://[A-Za-z0-9.-]+(:[0-9]+)?/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/(pull|issues)/(?<number>[0-9]+)#issuecomment-[0-9]+\z",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex CommentUrl();
}
