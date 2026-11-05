using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Issues;

public sealed class IssuePlanStatusReader : IIssuePlanStatusReader
{
    private const string PLAN_HEADING = "## Implementation Plan";

    private readonly IGitHubRestClient _restClient;

    public IssuePlanStatusReader(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public async ValueTask<IssuePlanStatus> ReadAsync(string repository, int issue, CancellationToken cancellationToken)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (issue <= 0)
        {
            throw ParameterValidationException.For(parameterName: nameof(issue), rule: "must be above 0");
        }

        string reference = $"{validRepository}#{issue}";

        try
        {
            IssueResponse response = await this._restClient.GetAsync(
                relativeUri: $"repos/{validRepository}/issues/{issue}",
                responseTypeInfo: GitHubJsonContext.Default.IssueResponse,
                cancellationToken: cancellationToken
            );

            // GitHub serves pull requests from the issues endpoint too, so one is reported as no such issue
            if (IsPullRequest(issue: issue, response: response))
            {
                throw new IssueNotFoundException(NotFoundMessage(reference));
            }

            bool blocked = IsBlocked(response.Labels);

            IReadOnlyList<IssueCommentResponse?> responses = await this._restClient.GetAllPagesAsync(
                relativeUri: $"repos/{validRepository}/issues/{issue}/comments?per_page=100",
                pageTypeInfo: GitHubJsonContext.Default.IssueCommentResponseArray,
                cancellationToken: cancellationToken
            );

            return Project(blocked: blocked, comments: [.. responses.Select(Comment)]);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new IssueNotFoundException(message: NotFoundMessage(reference), innerException: exception);
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new IssueViewFailedException(message: $"could not read issue {reference}", innerException: exception);
        }
    }

    private static IssuePlanStatus Project(bool blocked, IReadOnlyList<ParsedComment> comments)
    {
        int planIndex = LastPlanIndex(comments);

        if (planIndex < 0)
        {
            return new(HasPlan: false, Blocked: blocked, PlanAt: null, PlanBy: null, Comments: []);
        }

        ParsedComment plan = comments[planIndex];

        return new(
            HasPlan: true,
            Blocked: blocked,
            PlanAt: plan.FollowUp.CreatedAt,
            PlanBy: new(Login: plan.FollowUp.Login, Association: plan.FollowUp.Association, Url: plan.FollowUp.Url),
            Comments: [.. comments.Skip(planIndex + 1).Select(comment => comment.FollowUp)]
        );
    }

    private static int LastPlanIndex(IReadOnlyList<ParsedComment> comments)
    {
        for (int index = comments.Count - 1; index >= 0; --index)
        {
            if (comments[index].IsPlan)
            {
                return index;
            }
        }

        return -1;
    }

    private static ParsedComment Comment(IssueCommentResponse? response)
    {
        IssueCommentResponse comment =
            response ?? throw new GitHubInvalidResponseException("GitHub returned an empty comment");

        string createdAt =
            comment.CreatedAt is { } created && IssueCommentsProjector.TryParseTimestamp(value: created, out _)
                ? created
                : throw new GitHubInvalidResponseException("GitHub returned a comment without a valid creation time");

        Uri url = CommentUrl(comment.HtmlUrl);

        string association =
            comment.AuthorAssociation
            ?? throw new GitHubInvalidResponseException("GitHub returned a comment without an author association");

        string body =
            comment.Body ?? throw new GitHubInvalidResponseException("GitHub returned a comment without a body");

        return new(
            IsPlan: IsPlan(body),
            FollowUp: new(
                CreatedAt: createdAt,
                Login: UpstreamText.ReplaceLineBreaking(Login(comment.User)),
                Association: UpstreamText.ReplaceLineBreaking(association),
                HasApprovalKeyword: ApprovalKeywordMatcher.HasKeyword(body),
                Url: url
            )
        );
    }

    private static string Login(IssueCommentUserResponse? user)
    {
        if (user is null)
        {
            // GitHub returns no user for a comment whose author account was deleted
            return string.Empty;
        }

        return user.Login
            ?? throw new GitHubInvalidResponseException("GitHub returned a comment author without a login");
    }

    private static Uri CommentUrl(string? htmlUrl)
    {
        if (
            htmlUrl is null
            || !Uri.TryCreate(uriString: htmlUrl, uriKind: UriKind.Absolute, out Uri? uri)
            || !UriSchemes.IsHttp(uri)
        )
        {
            throw new GitHubInvalidResponseException("GitHub returned a comment without a valid link");
        }

        return uri;
    }

    private static bool IsPlan(string body)
    {
        return body.Split('\n').Any(line => StringComparer.Ordinal.Equals(x: line.TrimEnd('\r'), y: PLAN_HEADING));
    }

    private static bool IsBlocked(IssueLabelResponse?[]? labels)
    {
        return (labels ?? []).Any(label =>
            StringComparer.OrdinalIgnoreCase.Equals(
                x: label?.Name ?? throw new GitHubInvalidResponseException("GitHub returned a label without a name"),
                y: StandardLabels.BLOCKED
            )
        );
    }

    private static bool IsPullRequest(int issue, IssueResponse response)
    {
        return response.PullRequest is not null
            || response.HtmlUrl is { } htmlUrl
                && htmlUrl.EndsWith(value: $"/pull/{issue}", comparisonType: StringComparison.Ordinal);
    }

    private static string NotFoundMessage(string reference)
    {
        return $"issue {reference} was not found";
    }

    [DebuggerDisplay("IsPlan={IsPlan}, Url={FollowUp.Url}")]
    private readonly record struct ParsedComment(bool IsPlan, PlanFollowUpComment FollowUp);
}
