using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.PullRequests;

namespace Credfeto.Workflow.GitHub.ClosingIssues;

public sealed partial class ClosingIssueLabelsService : IClosingIssueLabelsService
{
    // GitHub returns at most 100 nodes per page, so this allows 10,000 entries in each list before giving up
    private const int MAX_PAGES = 100;
    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;
    private const string UNKNOWN_REFERENCE = "<unknown>";

    private const string CLOSING_ISSUES_QUERY =
        "query($o:String!,$r:String!,$n:Int!,$after:String){repository(owner:$o,name:$r){pullRequest(number:$n){closingIssuesReferences(first:100,after:$after){pageInfo{hasNextPage endCursor} nodes{number repository{owner{login} name} labels(first:100){pageInfo{hasNextPage endCursor} nodes{name}}}}}}}";

    private const string ISSUE_LABELS_QUERY =
        "query($o:String!,$r:String!,$n:Int!,$after:String){repository(owner:$o,name:$r){issue(number:$n){labels(first:100,after:$after){pageInfo{hasNextPage endCursor} nodes{name}}}}}";

    private readonly IGitHubGraphQlClient _graphQlClient;

    public ClosingIssueLabelsService(IGitHubGraphQlClient graphQlClient)
    {
        this._graphQlClient = graphQlClient;
    }

    public async ValueTask<ClosingIssueLabelsResponse> ReadAsync(
        string repository,
        int pullRequest,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        if (pullRequest <= 0)
        {
            throw ParameterValidationException.For(parameterName: nameof(pullRequest), rule: "must be above 0");
        }

        string reference = $"{validRepository}#{pullRequest}";

        try
        {
            return await this.ReadAllPagesAsync(
                repository: validRepository,
                pullRequest: pullRequest,
                reference: reference,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception is GitHubGraphQlException or GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new ClosingIssueLabelsFailedException(message: FailureMessage(reference), innerException: exception);
        }
    }

    private async ValueTask<ClosingIssueLabelsResponse> ReadAllPagesAsync(
        string repository,
        int pullRequest,
        string reference,
        CancellationToken cancellationToken
    )
    {
        (string owner, string name) = SplitRepository(repository);

        HashSet<string> labels = [with(StringComparer.Ordinal)];
        HashSet<string> failedReferences = [with(StringComparer.Ordinal)];

        // The cursor comes from the upstream, so a missing or repeated one must fail rather than loop forever
        HashSet<string> seen = [with(StringComparer.Ordinal)];
        string? after = null;

        for (int page = 0; page < MAX_PAGES; ++page)
        {
            ClosingIssuesQueryData data = await this._graphQlClient.ExecuteAsync(
                query: CLOSING_ISSUES_QUERY,
                variables: new ClosingIssuesQueryVariables(Owner: owner, Name: name, Number: pullRequest, After: after),
                variablesTypeInfo: GitHubJsonContext.Default.ClosingIssuesQueryVariables,
                dataTypeInfo: GitHubJsonContext.Default.ClosingIssuesQueryData,
                cancellationToken: cancellationToken
            );

            GraphQlConnection<ClosingIssueNode> connection =
                data.Repository?.PullRequest?.ClosingIssuesReferences ?? throw Failure(reference);

            foreach (ClosingIssueNode? node in connection.Nodes ?? [])
            {
                await this.CollectIssueLabelsAsync(
                    node: node,
                    labels: labels,
                    failedReferences: failedReferences,
                    cancellationToken: cancellationToken
                );
            }

            bool hasNextPage = connection.PageInfo?.HasNextPage ?? throw Failure(reference);

            if (!hasNextPage)
            {
                return BuildResponse(labels: labels, failedReferences: failedReferences);
            }

            after = connection.PageInfo.EndCursor;

            if (!GraphQlCursor.IsValid(after) || !seen.Add(after))
            {
                throw Failure(reference);
            }
        }

        throw Failure(reference);
    }

    private async ValueTask CollectIssueLabelsAsync(
        ClosingIssueNode? node,
        HashSet<string> labels,
        HashSet<string> failedReferences,
        CancellationToken cancellationToken
    )
    {
        if (!TryGetIssue(node: node, out IssueReference issue))
        {
            failedReferences.Add(DescribeReference(node));

            return;
        }

        IReadOnlyList<string>? issueLabels = await this.ReadIssueLabelsAsync(
            issue: issue,
            inline: node.Labels,
            cancellationToken: cancellationToken
        );

        if (issueLabels is null)
        {
            failedReferences.Add(issue.Reference);

            return;
        }

        labels.UnionWith(issueLabels.Where(IsIncluded));
    }

    private async ValueTask<IReadOnlyList<string>?> ReadIssueLabelsAsync(
        IssueReference issue,
        GraphQlConnection<LabelNode>? inline,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await this.ReadAllLabelPagesAsync(
                issue: issue,
                inline: inline,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception is GitHubGraphQlException or GitHubInvalidResponseException or HttpRequestException)
        {
            // One unreadable issue must not hide the labels of the others; it is reported as a failed reference
            return null;
        }
    }

    private async ValueTask<IReadOnlyList<string>?> ReadAllLabelPagesAsync(
        IssueReference issue,
        GraphQlConnection<LabelNode>? inline,
        CancellationToken cancellationToken
    )
    {
        List<string> names = [];
        HashSet<string> seen = [with(StringComparer.Ordinal)];
        GraphQlConnection<LabelNode>? connection = inline;

        for (int page = 0; page < MAX_PAGES; ++page)
        {
            if (connection is null)
            {
                return null;
            }

            names.AddRange(LabelNames(connection));

            bool? hasNextPage = connection.PageInfo?.HasNextPage;

            if (hasNextPage is not true)
            {
                return hasNextPage is null ? null : names;
            }

            string? after = connection.PageInfo?.EndCursor;

            if (!GraphQlCursor.IsValid(after) || !seen.Add(after))
            {
                return null;
            }

            IssueLabelsQueryData data = await this._graphQlClient.ExecuteAsync(
                query: ISSUE_LABELS_QUERY,
                variables: new ClosingIssuesQueryVariables(
                    Owner: issue.Owner,
                    Name: issue.Name,
                    Number: issue.Number,
                    After: after
                ),
                variablesTypeInfo: GitHubJsonContext.Default.ClosingIssuesQueryVariables,
                dataTypeInfo: GitHubJsonContext.Default.IssueLabelsQueryData,
                cancellationToken: cancellationToken
            );

            connection = data.Repository?.Issue?.Labels;
        }

        return null;
    }

    private static IEnumerable<string> LabelNames(GraphQlConnection<LabelNode> connection)
    {
        return (connection.Nodes ?? [])
            .Select(static label => label?.Name)
            .Where(static label => !string.IsNullOrEmpty(label))
            .Select(static label => label!);
    }

    private static bool TryGetIssue([NotNullWhen(true)] ClosingIssueNode? node, out IssueReference issue)
    {
        string? owner = node?.Repository?.Owner?.Login;
        string? name = node?.Repository?.Name;
        int number = node?.Number ?? 0;

        if (owner is null || name is null || number <= 0 || !ReferenceRepository().IsMatch($"{owner}/{name}"))
        {
            issue = default;

            return false;
        }

        issue = new(Owner: owner, Name: name, Number: number);

        return true;
    }

    private static string DescribeReference(ClosingIssueNode? node)
    {
        string? owner = node?.Repository?.Owner?.Login;
        string? name = node?.Repository?.Name;
        int? number = node?.Number;

        return owner is null || name is null || number is null ? UNKNOWN_REFERENCE : $"{owner}/{name}#{number}";
    }

    private static bool IsIncluded(string label)
    {
        // Workflow-control labels belong to the issue alone and must never be copied onto the pull request
        return !StringComparer.Ordinal.Equals(x: label, y: "Blocked")
            && !StringComparer.Ordinal.Equals(x: label, y: "On-Hold");
    }

    private static ClosingIssueLabelsResponse BuildResponse(HashSet<string> labels, HashSet<string> failedReferences)
    {
        return new(
            Labels: [.. labels.Order(StringComparer.Ordinal)],
            Complete: failedReferences.Count == 0,
            FailedReferences: [.. failedReferences.Order(StringComparer.Ordinal)]
        );
    }

    private static (string Owner, string Name) SplitRepository(string repository)
    {
        // GitHubNames.Repository guarantees exactly one separator between a non-empty owner and name
        int separator = repository.IndexOf('/', StringComparison.Ordinal);

        return (repository[..separator], repository[(separator + 1)..]);
    }

    private static ClosingIssueLabelsFailedException Failure(string reference)
    {
        return new(FailureMessage(reference));
    }

    private static string FailureMessage(string reference)
    {
        return $"could not read the closing issues of {reference}";
    }

    // \z rather than $ because in .NET $ also matches before a trailing newline, which would let one through
    [GeneratedRegex(
        pattern: @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex ReferenceRepository();

    [DebuggerDisplay("{Reference}")]
    private readonly record struct IssueReference(string Owner, string Name, int Number)
    {
        public string Reference => $"{this.Owner}/{this.Name}#{this.Number}";
    }
}
