using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.BodyRules;
using Credfeto.Workflow.GitHub.Checks;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.Leaks;

namespace Credfeto.Workflow.GitHub.PullRequests;

public sealed class PrReviewCommentService : IPrReviewCommentService
{
    private const string RIGHT = "RIGHT";

    private const string LEFT = "LEFT";

    private const string READ_FAILED = "the review comment was not posted: could not read the pull request";

    private const string PARENT_SEGMENT = "..";

    private const string CURRENT_SEGMENT = ".";

    private readonly ILeakChecker _leakChecker;

    private readonly IGitHubRestClient _restClient;

    public PrReviewCommentService(IGitHubRestClient restClient, ILeakChecker leakChecker)
    {
        this._restClient = restClient;
        this._leakChecker = leakChecker;
    }

    public ValueTask<PrReviewCommentResult> PostAsync(
        string repository,
        int pullRequest,
        string path,
        int line,
        int? startLine,
        string? side,
        string body,
        CancellationToken cancellationToken
    )
    {
        // Every argument is validated before the first GitHub call, so invalid input never costs one and is reported
        // as such rather than as a GitHub failure
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (pullRequest <= 0)
        {
            throw ParameterValidationException.For(parameterName: nameof(pullRequest), rule: "must be above 0");
        }

        ValidatePath(path);
        ValidateLines(line: line, startLine: startLine);
        string validSide = ValidSide(side);

        if (string.IsNullOrWhiteSpace(body))
        {
            throw ParameterValidationException.For(
                parameterName: nameof(body),
                rule: "it must not be empty or whitespace"
            );
        }

        return this.CheckAndPostAsync(
            new(
                ValidRepository: validRepository,
                PullRequest: pullRequest,
                Path: path,
                Line: line,
                StartLine: startLine,
                Side: validSide,
                Body: body
            ),
            cancellationToken: cancellationToken
        );
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw ParameterValidationException.For(
                parameterName: nameof(path),
                rule: "it must not be empty or whitespace"
            );
        }

        if (GitRefFormat.ContainsControlCharacter(path))
        {
            throw ParameterValidationException.For(
                parameterName: nameof(path),
                rule: "it must not contain a control character"
            );
        }

        // GitHub names a file by its path from the repository root with / between the segments, so anything else
        // could never match a file in the diff
        if (path.Contains('\\', StringComparison.Ordinal) || path[0] == '/')
        {
            throw ParameterValidationException.For(
                parameterName: nameof(path),
                rule: "it must be relative to the repository root, with / between segments"
            );
        }

        foreach (string segment in path.Split('/'))
        {
            if (
                segment.Length == 0
                || StringComparer.Ordinal.Equals(x: segment, y: CURRENT_SEGMENT)
                || StringComparer.Ordinal.Equals(x: segment, y: PARENT_SEGMENT)
            )
            {
                throw ParameterValidationException.For(
                    parameterName: nameof(path),
                    rule: "it must not contain an empty, . or .. segment"
                );
            }
        }
    }

    private static void ValidateLines(int line, int? startLine)
    {
        if (line <= 0)
        {
            throw ParameterValidationException.For(parameterName: nameof(line), rule: "must be above 0");
        }

        if (startLine is { } start && (start <= 0 || start >= line))
        {
            throw ParameterValidationException.For(
                parameterName: nameof(startLine),
                rule: "must be above 0 and below line"
            );
        }
    }

    private static string ValidSide(string? side)
    {
        if (side is null)
        {
            return RIGHT;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(x: side, y: RIGHT))
        {
            return RIGHT;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(x: side, y: LEFT))
        {
            return LEFT;
        }

        throw ParameterValidationException.For(parameterName: nameof(side), rule: "must be RIGHT or LEFT");
    }

    private async ValueTask<PrReviewCommentResult> CheckAndPostAsync(
        ReviewComment comment,
        CancellationToken cancellationToken
    )
    {
        // A refused path or comment text is never echoed back: only the rule and line number are reported
        if (this._leakChecker.CheckValue(subject: LeakSubject.TEXT, value: comment.Path) is { } pathLeak)
        {
            return Refused(
                rule: $"the path must not contain {LeakRuleNames.Name(pathLeak.Rule)}",
                lineNumber: pathLeak.LineNumber
            );
        }

        LeakRefusal? bodyLeak = this._leakChecker.CheckBody(comment.Body)
            ?? await this._leakChecker.CheckRepositoryReferencesAsync(
                repository: comment.ValidRepository,
                subject: LeakSubject.BODY,
                text: comment.Body,
                cancellationToken: cancellationToken
            );

        if (bodyLeak is not null)
        {
            return Refused(
                rule: $"the comment must not contain {LeakRuleNames.Name(bodyLeak.Rule)}",
                lineNumber: bodyLeak.LineNumber
            );
        }

        // Such a comment could be read as a human approving a plan
        if (ApprovalKeywordMatcher.FindFirstKeywordLine(comment.Body) is { } approval)
        {
            return Refused(rule: "the comment must not contain an approval keyword", lineNumber: approval.LineNumber);
        }

        return await this.ReadHeadAndPostAsync(comment: comment, cancellationToken: cancellationToken);
    }

    private async ValueTask<PrReviewCommentResult> ReadHeadAndPostAsync(
        ReviewComment comment,
        CancellationToken cancellationToken
    )
    {
        PullRequestHeadResponse pullRequest;

        try
        {
            pullRequest = await this._restClient.GetAsync(
                relativeUri: $"repos/{comment.ValidRepository}/pulls/{comment.PullRequest.ToString(CultureInfo.InvariantCulture)}",
                responseTypeInfo: GitHubJsonContext.Default.PullRequestHeadResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return Failed(
                outcome: PrReviewCommentOutcome.NotFound,
                message: $"the pull request {comment.ValidRepository}#{comment.PullRequest.ToString(CultureInfo.InvariantCulture)} was not found"
            );
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            return Failed(outcome: PrReviewCommentOutcome.NotPosted, message: READ_FAILED);
        }

        // The comment is anchored to the head commit, so the diff it is placed on is the one the pull request shows
        if (pullRequest is not { Head.Sha: { Length: > 0 } headSha })
        {
            return Failed(outcome: PrReviewCommentOutcome.NotPosted, message: READ_FAILED);
        }

        return await this.PostCommentAsync(comment: comment, headSha: headSha, cancellationToken: cancellationToken);
    }

    private async ValueTask<PrReviewCommentResult> PostCommentAsync(
        ReviewComment comment,
        string headSha,
        CancellationToken cancellationToken
    )
    {
        ReviewCommentPostAnswer answer = await ReviewCommentPoster.PostAsync(
            restClient: this._restClient,
            relativeUri: $"repos/{comment.ValidRepository}/pulls/{comment.PullRequest.ToString(CultureInfo.InvariantCulture)}/comments",
            body: new PullRequestReviewCommentCreateRequest(
                Body: comment.Body,
                CommitId: headSha,
                Path: comment.Path,
                Line: comment.Line,
                Side: comment.Side,
                StartLine: comment.StartLine,
                StartSide: comment.StartLine is null ? null : comment.Side
            ),
            requestTypeInfo: GitHubJsonContext.Default.PullRequestReviewCommentCreateRequest,
            validRepository: comment.ValidRepository,
            pullRequest: comment.PullRequest,
            cancellationToken: cancellationToken
        );

        return answer switch
        {
            { CommentId: long postedId, Url: { } url } => new(
                Outcome: PrReviewCommentOutcome.Posted,
                Message: $"the review comment was posted on {comment.Path} {Lines(comment)}",
                CommentId: postedId,
                Url: url,
                Rule: null,
                LineNumber: null
            ),
            { Rejected: true } => Failed(
                outcome: PrReviewCommentOutcome.NotPosted,
                message: $"the review comment was not posted: {answer.Error}"
            ),
            _ => Failed(
                outcome: PrReviewCommentOutcome.MayHaveBeenPosted,
                message: $"the review comment may have been posted ({answer.Error}); use the pr_comments tool and look for it before posting again"
            ),
        };
    }

    private static string Lines(ReviewComment comment)
    {
        string line = comment.Line.ToString(CultureInfo.InvariantCulture);

        return comment.StartLine is { } start
            ? $"lines {start.ToString(CultureInfo.InvariantCulture)}-{line} ({comment.Side})"
            : $"line {line} ({comment.Side})";
    }

    private static PrReviewCommentResult Refused(string rule, int? lineNumber)
    {
        return new(
            Outcome: PrReviewCommentOutcome.Refused,
            Message: "the review comment was refused",
            CommentId: null,
            Url: null,
            Rule: rule,
            LineNumber: lineNumber
        );
    }

    private static PrReviewCommentResult Failed(PrReviewCommentOutcome outcome, string message)
    {
        return new(Outcome: outcome, Message: message, CommentId: null, Url: null, Rule: null, LineNumber: null);
    }

    [DebuggerDisplay("{ValidRepository}#{PullRequest} {Path}:{Line}")]
    private sealed record ReviewComment(
        string ValidRepository,
        int PullRequest,
        string Path,
        int Line,
        int? StartLine,
        string Side,
        string Body
    );
}
