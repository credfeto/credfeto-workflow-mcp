using System;
using System.Collections.Generic;
using System.Globalization;
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
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Labels;

public sealed class LabelHistoryReader : ILabelHistoryReader
{
    private const int MAX_LABEL_CHANGES = 1000;
    private const string LABELED = "labeled";
    private const string UNLABELED = "unlabeled";
    private const string ADDED = "added";
    private const string REMOVED = "removed";
    private const string NONE = "-";

    private readonly IGitHubRestClient _restClient;

    public LabelHistoryReader(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public ValueTask<LabelHistoryResult> ReadAsync(
        string repository,
        int? issue,
        int? pr,
        string? label,
        CancellationToken cancellationToken
    )
    {
        // Everything is checked before the first request is made
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        (int number, bool isPullRequest) = Target(issue: issue, pr: pr);
        string? validLabel = label is null ? null : GitHubNames.LabelFilter(value: label, parameterName: nameof(label));

        return this.ReadAsync(
            validRepository: validRepository,
            number: number,
            isPullRequest: isPullRequest,
            label: validLabel,
            cancellationToken: cancellationToken
        );
    }

    private static (int Number, bool IsPullRequest) Target(int? issue, int? pr)
    {
        if (issue is null == pr is null)
        {
            throw ParameterValidationException.For(
                parameterName: nameof(pr),
                rule: "exactly one of pr or issue must be given"
            );
        }

        if (pr is { } pullRequest)
        {
            return (Positive(value: pullRequest, parameterName: nameof(pr)), true);
        }

        return (Positive(value: issue.GetValueOrDefault(), parameterName: nameof(issue)), false);
    }

    private static int Positive(int value, string parameterName)
    {
        return value > 0
            ? value
            : throw ParameterValidationException.For(parameterName: parameterName, rule: "must be above 0");
    }

    private async ValueTask<LabelHistoryResult> ReadAsync(
        string validRepository,
        int number,
        bool isPullRequest,
        string? label,
        CancellationToken cancellationToken
    )
    {
        try
        {
            IssueResponse item = await this._restClient.GetAsync(
                relativeUri: $"repos/{validRepository}/issues/{number}",
                responseTypeInfo: GitHubJsonContext.Default.IssueResponse,
                cancellationToken: cancellationToken
            );

            // GitHub serves pull requests from the issues endpoint too, so the other kind of item is not found
            if (IssueKind.IsPullRequest(issue: number, response: item) != isPullRequest)
            {
                throw NotFound(repository: validRepository, number: number, isPullRequest: isPullRequest, inner: null);
            }

            IReadOnlyList<LabelHistoryEventResponse> events = await this._restClient.GetAllPagesAsync(
                relativeUri: $"repos/{validRepository}/issues/{number}/timeline?per_page=100",
                pageTypeInfo: GitHubJsonContext.Default.LabelHistoryEventResponseArray,
                cancellationToken: cancellationToken
            );

            LabelHistoryEventResponse[] changes = [.. events.Where(static e => IsLabelChange(e.Event))];

            // Reading only part of the history would silently hide the rest, so too many is refused before any filter
            if (changes.Length > MAX_LABEL_CHANGES)
            {
                throw new LabelHistoryFailedException(
                    $"{validRepository}#{number.ToString(CultureInfo.InvariantCulture)} has more than {MAX_LABEL_CHANGES} label changes to read"
                );
            }

            return new(
                Repository: validRepository,
                Number: number,
                Changes: [.. changes.Where(change => Matches(change: change, filter: label)).Select(Project)]
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw NotFound(repository: validRepository, number: number, isPullRequest: isPullRequest, inner: exception);
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new LabelHistoryFailedException(
                message: $"could not read the label history of {validRepository}#{number.ToString(CultureInfo.InvariantCulture)}",
                innerException: exception
            );
        }
    }

    private static Exception NotFound(string repository, int number, bool isPullRequest, Exception? inner)
    {
        string reference = $"{repository}#{number.ToString(CultureInfo.InvariantCulture)}";

        return isPullRequest
            ? new PullRequestNotFoundException(
                message: $"the pull request {reference} was not found, or the caller cannot see it",
                innerException: inner
            )
            : new IssueNotFoundException(
                message: $"the issue {reference} was not found, or the caller cannot see it",
                innerException: inner
            );
    }

    private static bool IsLabelChange(string? kind)
    {
        return StringComparer.Ordinal.Equals(x: kind, y: LABELED)
            || StringComparer.Ordinal.Equals(x: kind, y: UNLABELED);
    }

    private static bool Matches(LabelHistoryEventResponse change, string? filter)
    {
        return filter is null || change.Label?.Name is { } name && EqualsIgnoringAsciiCase(left: name, right: filter);
    }

    // Only the letters A-Z fold; any other character, including a non-ASCII letter, must match exactly
    private static bool EqualsIgnoringAsciiCase(string left, string right)
    {
        return left.Length == right.Length
            && left.Zip(right).All(static pair => SameCharacter(pair.First, pair.Second));
    }

    private static bool SameCharacter(char left, char right)
    {
        return left == right
            || char.IsAsciiLetter(left) && char.IsAsciiLetter(right) && (left | 0x20) == (right | 0x20);
    }

    private static LabelHistoryEntry Project(LabelHistoryEventResponse change)
    {
        string time =
            change.CreatedAt is { } createdAt && IssueCommentsProjector.TryParseTimestamp(value: createdAt, out _)
                ? createdAt
                : throw new GitHubInvalidResponseException("GitHub returned a label change without a valid time");

        return new(
            Action: StringComparer.Ordinal.Equals(x: change.Event, y: LABELED) ? ADDED : REMOVED,
            Time: time,
            Actor: Text(change.Actor?.Login),
            Label: Text(change.Label?.Name)
        );
    }

    private static string Text(string? value)
    {
        return string.IsNullOrEmpty(value) ? NONE : UpstreamText.ReplaceLineBreaking(value);
    }
}
