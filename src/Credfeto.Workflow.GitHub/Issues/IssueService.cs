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
