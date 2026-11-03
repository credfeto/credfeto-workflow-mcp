using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Labels;

public sealed class LabelService : ILabelService
{
    private const string PRIORITY_RULE = "a priority label may only be changed by a human";
    private const string ON_HOLD_RULE = "On Hold may only be removed by a human";
    private const string BLOCKED_RULE = "Blocked may only be removed when it was last added by this agent";

    private readonly IGitHubRestClient _restClient;

    public LabelService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public ValueTask<LabelChangeResponse> AddAsync(
        string repository,
        int? pullRequest,
        int? issue,
        IReadOnlyList<string> labels,
        CancellationToken cancellationToken
    )
    {
        LabelTarget target = Target(repository: repository, pullRequest: pullRequest, issue: issue);
        IReadOnlyList<string> validLabels = ValidLabels(labels: labels, removing: false);

        return this.AddLabelsAsync(target: target, validLabels: validLabels, cancellationToken: cancellationToken);
    }

    public ValueTask<LabelChangeResponse> RemoveAsync(
        string repository,
        int? pullRequest,
        int? issue,
        IReadOnlyList<string> labels,
        CancellationToken cancellationToken
    )
    {
        LabelTarget target = Target(repository: repository, pullRequest: pullRequest, issue: issue);
        IReadOnlyList<string> validLabels = ValidLabels(labels: labels, removing: true);

        return this.RemoveLabelsAsync(target: target, validLabels: validLabels, cancellationToken: cancellationToken);
    }

    private async ValueTask<LabelChangeResponse> AddLabelsAsync(
        LabelTarget target,
        IReadOnlyList<string> validLabels,
        CancellationToken cancellationToken
    )
    {
        try
        {
            IReadOnlyList<string> current = [];

            // One label per call adds to the existing set; a single call with every label could not replace them either, but stays easy to reason about
            foreach (string label in validLabels)
            {
                IssueLabelResponse?[] response = await this._restClient.PostAsync(
                    relativeUri: IssueLabelsUri(target),
                    body: new LabelAddRequest([label]),
                    requestTypeInfo: GitHubJsonContext.Default.LabelAddRequest,
                    responseTypeInfo: GitHubJsonContext.Default.IssueLabelResponseArray,
                    cancellationToken: cancellationToken
                );

                current = LabelNames(response);
            }

            return Response(target: target, labels: current);
        }
        catch (Exception exception) when (IsUpstreamFailure(exception))
        {
            throw Failure(target: target, exception: exception);
        }
    }

    private async ValueTask<LabelChangeResponse> RemoveLabelsAsync(
        LabelTarget target,
        IReadOnlyList<string> validLabels,
        CancellationToken cancellationToken
    )
    {
        try
        {
            IReadOnlyList<string> current = await this.CurrentLabelsAsync(
                target: target,
                cancellationToken: cancellationToken
            );

            IReadOnlyList<string> present =
            [
                .. validLabels.Where(label =>
                    current.Contains(value: label, comparer: StringComparer.OrdinalIgnoreCase)
                ),
            ];

            if (present.Contains(value: StandardLabels.BLOCKED, comparer: StringComparer.OrdinalIgnoreCase))
            {
                await this.EnsureBlockedAddedByAgentAsync(target: target, cancellationToken: cancellationToken);
            }

            foreach (string label in present)
            {
                IssueLabelResponse?[] response = await this._restClient.DeleteAsync(
                    relativeUri: IssueLabelsUri(target) + "/" + Uri.EscapeDataString(label),
                    responseTypeInfo: GitHubJsonContext.Default.IssueLabelResponseArray,
                    cancellationToken: cancellationToken
                );

                current = LabelNames(response);
            }

            return Response(target: target, labels: current);
        }
        catch (Exception exception) when (IsUpstreamFailure(exception))
        {
            throw Failure(target: target, exception: exception);
        }
    }

    private async ValueTask<IReadOnlyList<string>> CurrentLabelsAsync(
        LabelTarget target,
        CancellationToken cancellationToken
    )
    {
        IssueLabelResponse?[] response = await this._restClient.GetAsync(
            relativeUri: IssueLabelsUri(target) + "?per_page=100",
            responseTypeInfo: GitHubJsonContext.Default.IssueLabelResponseArray,
            cancellationToken: cancellationToken
        );

        return LabelNames(response);
    }

    // Only the most recent time Blocked was added counts: a human re-adding it after the agent takes ownership back
    private async ValueTask EnsureBlockedAddedByAgentAsync(LabelTarget target, CancellationToken cancellationToken)
    {
        IssueUserResponse user = await this._restClient.GetAsync(
            relativeUri: "user",
            responseTypeInfo: GitHubJsonContext.Default.IssueUserResponse,
            cancellationToken: cancellationToken
        );

        string agent = user.Login ?? throw new GitHubInvalidResponseException("GitHub returned no login for the agent");

        IReadOnlyList<LabelTimelineEventResponse?> timeline = await this._restClient.GetAllPagesAsync(
            relativeUri: $"repos/{target.Repository}/issues/{target.Number.ToString(CultureInfo.InvariantCulture)}/timeline?per_page=100",
            pageTypeInfo: GitHubJsonContext.Default.LabelTimelineEventResponseArray,
            cancellationToken: cancellationToken
        );

        LabelTimelineEventResponse? lastBlocked = timeline.LastOrDefault(IsBlockedAdded);

        if (!StringComparer.OrdinalIgnoreCase.Equals(x: lastBlocked?.Actor?.Login, y: agent))
        {
            throw Refused(label: StandardLabels.BLOCKED, rule: BLOCKED_RULE);
        }
    }

    private static bool IsBlockedAdded(LabelTimelineEventResponse? timelineEvent)
    {
        return StringComparer.Ordinal.Equals(x: timelineEvent?.Event, y: "labeled")
            && StringComparer.OrdinalIgnoreCase.Equals(x: timelineEvent.Label?.Name, y: StandardLabels.BLOCKED);
    }

    private static LabelTarget Target(string repository, int? pullRequest, int? issue)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (pullRequest.HasValue == issue.HasValue)
        {
            throw ParameterValidationException.For(
                parameterName: "pr",
                rule: "exactly one of pr or issue must be given"
            );
        }

        if (pullRequest is { } pullRequestNumber)
        {
            return pullRequestNumber <= 0
                ? throw ParameterValidationException.For(parameterName: nameof(pullRequest), rule: "must be above 0")
                : new(Repository: validRepository, Number: pullRequestNumber, IsPullRequest: true);
        }

        int issueNumber = issue.GetValueOrDefault();

        return issueNumber <= 0
            ? throw ParameterValidationException.For(parameterName: nameof(issue), rule: "must be above 0")
            : new(Repository: validRepository, Number: issueNumber, IsPullRequest: false);
    }

    private static IReadOnlyList<string> ValidLabels(IReadOnlyList<string> labels, bool removing)
    {
        if (labels.Count == 0)
        {
            throw ParameterValidationException.For(
                parameterName: nameof(labels),
                rule: "at least one label must be given"
            );
        }

        IReadOnlyList<string> validLabels =
        [
            .. labels
                .Select(label => GitHubNames.StandardLabel(value: label, parameterName: nameof(labels)))
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];

        foreach (string label in validLabels)
        {
            if (StandardLabels.IsPriority(label))
            {
                throw Refused(label: label, rule: PRIORITY_RULE);
            }

            if (removing && StringComparer.OrdinalIgnoreCase.Equals(x: label, y: StandardLabels.ON_HOLD))
            {
                throw Refused(label: label, rule: ON_HOLD_RULE);
            }
        }

        return validLabels;
    }

    private static LabelRefusedException Refused(string label, string rule)
    {
        return new($"the label {label} was refused: {rule}") { Label = label, Rule = rule };
    }

    private static string IssueLabelsUri(LabelTarget target)
    {
        return $"repos/{target.Repository}/issues/{target.Number.ToString(CultureInfo.InvariantCulture)}/labels";
    }

    private static IReadOnlyList<string> LabelNames(IssueLabelResponse?[]? response)
    {
        return
        [
            .. (response ?? throw new GitHubInvalidResponseException("GitHub returned no labels")).Select(label =>
                label?.Name ?? throw new GitHubInvalidResponseException("GitHub returned an empty label")
            ),
        ];
    }

    private static LabelChangeResponse Response(LabelTarget target, IReadOnlyList<string> labels)
    {
        return new(Repository: target.Repository, Number: target.Number, Labels: labels);
    }

    private static bool IsUpstreamFailure(Exception exception)
    {
        return exception is GitHubInvalidResponseException or HttpRequestException;
    }

    private static Exception Failure(LabelTarget target, Exception exception)
    {
        if (exception is HttpRequestException { StatusCode: HttpStatusCode.NotFound })
        {
            return target.IsPullRequest
                ? new PullRequestNotFoundException(
                    message: $"the pull request {target.Repository}#{target.Number.ToString(CultureInfo.InvariantCulture)} was not found, or the caller cannot see it",
                    innerException: exception
                )
                : new IssueNotFoundException(
                    message: $"the issue {target.Repository}#{target.Number.ToString(CultureInfo.InvariantCulture)} was not found, or the caller cannot see it",
                    innerException: exception
                );
        }

        // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
        return new LabelChangeFailedException(
            message: $"could not change the labels of {target.Repository}#{target.Number.ToString(CultureInfo.InvariantCulture)}",
            innerException: exception
        );
    }
}
