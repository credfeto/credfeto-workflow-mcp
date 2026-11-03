using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Issues;

public sealed class IssueService : IIssueService
{
    private const string OPEN = "OPEN";

    private const string CLOSED = "CLOSED";

    private const string APP_PREFIX = "app/";

    private const int MAX_COMMENTS_READ = 10000;

    private readonly IGitHubRestClient _restClient;

    public IssueService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public async ValueTask<IssueViewResponse> ViewAsync(
        string repository,
        int issue,
        CancellationToken cancellationToken
    )
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

            return Project(reference: reference, issue: issue, response: response);
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

    public async ValueTask<IssueComments> CommentsAsync(
        string repository,
        int issue,
        string? since,
        string? author,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (issue <= 0)
        {
            throw ParameterValidationException.For(parameterName: nameof(issue), rule: "must be above 0");
        }

        DateTime? validSince = Since(since);
        string? validAuthor = Author(author);
        string reference = $"{validRepository}#{issue}";

        try
        {
            IssueResponse response = await this._restClient.GetAsync(
                relativeUri: $"repos/{validRepository}/issues/{issue}",
                responseTypeInfo: GitHubJsonContext.Default.IssueResponse,
                cancellationToken: cancellationToken
            );

            Uri url = IssueUrl(response.HtmlUrl);
            int expected = ExpectedCommentCount(reference: reference, response: response);

            IReadOnlyList<IssueCommentResponse?> comments = await this._restClient.GetAllPagesAsync(
                relativeUri: $"repos/{validRepository}/issues/{issue}/comments?per_page=100",
                pageTypeInfo: GitHubJsonContext.Default.IssueCommentResponseArray,
                cancellationToken: cancellationToken
            );

            // A comment added or deleted between the two reads would leave a partial list, which is refused
            if (comments.Count != expected)
            {
                throw IssueCommentsFailure.Incomplete(reference);
            }

            return new IssueCommentsProjector(reference).Project(
                url: url,
                responses: comments,
                since: validSince,
                author: validAuthor
            );
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

    private static DateTime? Since(string? since)
    {
        if (since is null)
        {
            return null;
        }

        if (!IssueCommentsProjector.TryParseTimestamp(value: since, out DateTime timestamp))
        {
            throw ParameterValidationException.For(
                parameterName: nameof(since),
                rule: "it must be a UTC time in the form yyyy-MM-ddTHH:mm:ssZ"
            );
        }

        return timestamp;
    }

    private static string? Author(string? author)
    {
        if (author is null)
        {
            return null;
        }

        bool isApp = author.StartsWith(value: APP_PREFIX, comparisonType: StringComparison.Ordinal);
        string name = GitHubNames.Login(
            value: isApp ? author[APP_PREFIX.Length..] : author,
            parameterName: nameof(author)
        );

        // A bot is printed as app/<slug>, so a '[bot]' login or a name GitHub cannot issue would never match
        if (
            name.EndsWith(value: IssueCommentsProjector.BOT_SUFFIX, comparisonType: StringComparison.Ordinal)
            || name[0] == '-'
        )
        {
            throw ParameterValidationException.For(
                parameterName: nameof(author),
                rule: "it must be a login, or app/<slug> for a GitHub App, without '[bot]' or a leading '-'"
            );
        }

        return author;
    }

    private static Uri IssueUrl(string? htmlUrl)
    {
        if (
            htmlUrl is null
            || !Uri.TryCreate(uriString: htmlUrl, uriKind: UriKind.Absolute, out Uri? uri)
            || !UriSchemes.IsHttp(uri)
        )
        {
            throw new GitHubInvalidResponseException("GitHub returned an issue without a valid link");
        }

        return uri;
    }

    private static int ExpectedCommentCount(string reference, IssueResponse response)
    {
        int count =
            response.Comments
            ?? throw new GitHubInvalidResponseException("GitHub returned an issue without a comment count");

        return count is >= 0 and <= MAX_COMMENTS_READ ? count : throw IssueCommentsFailure.Incomplete(reference);
    }

    private static string NotFoundMessage(string reference)
    {
        return $"issue {reference} was not found";
    }

    private static IssueViewResponse Project(string reference, int issue, IssueResponse response)
    {
        // GitHub serves pull requests from the issues endpoint too, so one is reported as no such issue
        if (IsPullRequest(issue: issue, response: response))
        {
            throw new IssueNotFoundException(NotFoundMessage(reference));
        }

        string title =
            response.Title ?? throw new GitHubInvalidResponseException("GitHub returned an issue without a title");

        return new(
            Issue: reference,
            Title: UpstreamText.ReplaceLineBreaking(title),
            State: State(response.State),
            Labels: Labels(response.Labels),
            Assignees: Assignees(response.Assignees),
            Body: response.Body ?? string.Empty
        );
    }

    private static bool IsPullRequest(int issue, IssueResponse response)
    {
        return response.PullRequest is not null
            || response.HtmlUrl is { } htmlUrl
                && htmlUrl.EndsWith(value: $"/pull/{issue}", comparisonType: StringComparison.Ordinal);
    }

    private static string State(string? state)
    {
        return state switch
        {
            "open" => OPEN,
            "closed" => CLOSED,
            _ => throw new GitHubInvalidResponseException("GitHub returned an issue with an unknown state"),
        };
    }

    private static IReadOnlyList<string> Labels(IReadOnlyList<IssueLabelResponse?>? labels)
    {
        return
        [
            .. (labels ?? []).Select(label =>
                UpstreamText.ReplaceLineBreaking(
                    label?.Name ?? throw new GitHubInvalidResponseException("GitHub returned a label without a name")
                )
            ),
        ];
    }

    private static IReadOnlyList<string> Assignees(IReadOnlyList<IssueUserResponse?>? assignees)
    {
        return
        [
            .. (assignees ?? []).Select(assignee =>
                UpstreamText.ReplaceLineBreaking(
                    assignee?.Login
                        ?? throw new GitHubInvalidResponseException("GitHub returned an assignee without a login")
                )
            ),
        ];
    }
}
